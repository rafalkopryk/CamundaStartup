using System.Collections.Concurrent;
using System.Net;
using Camunda.Orchestration.Sdk;

namespace Camunda.Startup.DemoApp.Feature;

/// <summary>
/// Simulates users during unattended load tests while retaining Camunda-managed user tasks.
/// Only tasks belonging to the weather sizing process definitions are completed.
/// </summary>
public sealed class AutoCompleteManagedUserTasksService : BackgroundService
{
    private readonly CamundaClient client;
    private readonly ILogger<AutoCompleteManagedUserTasksService> logger;
    private readonly int completionConcurrency;
    private readonly SemaphoreSlim completionSlots;
    private readonly ConcurrentDictionary<string, DateTimeOffset> recentlyHandledTasks = new();
    private readonly ConcurrentDictionary<string, EndCursor> searchCursors = new();

    private static readonly TimeSpan HandledTaskRetention = TimeSpan.FromMinutes(5);
    private const int PageSize = 100;

    private static readonly string[] ProcessDefinitionIds =
    [
        "weather-forecast-collect",
        "weather-forecast-validate",
        "weather-forecast-deliver",
    ];

    public AutoCompleteManagedUserTasksService(
        IConfiguration configuration,
        CamundaClient client,
        ILogger<AutoCompleteManagedUserTasksService> logger)
    {
        completionConcurrency = configuration.GetValue("LoadTest:UserTaskCompletionConcurrency", 3);
        completionSlots = new SemaphoreSlim(completionConcurrency, completionConcurrency);
        this.client = client;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RemoveExpiredHandledTasks();

                var completed = await Task.WhenAll(ProcessDefinitionIds.Select(processDefinitionId =>
                    CompleteCreatedTasks(processDefinitionId, stoppingToken)));

                if (completed.Sum() == 0)
                    await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to auto-complete managed weather user tasks");
                await Task.Delay(100, stoppingToken);
            }
        }
    }

    private async Task<int> CompleteCreatedTasks(string processDefinitionId, CancellationToken ct)
    {
        SearchQueryPageRequest page = searchCursors.TryGetValue(processDefinitionId, out var cursor)
            ? new CursorForwardPagination
            {
                After = cursor,
                Limit = PageSize
            }
            : new LimitPagination
            {
                Limit = PageSize
            };

        UserTaskSearchQueryResult response;
        try
        {
            response = await client.SearchUserTasksAsync(new UserTaskSearchQuery
            {
                Sort =
                [
                    new UserTaskSearchQuerySortRequest
                    {
                        Field = "creationDate",
                        Order = SortOrderEnum.ASC,
                    }
                ],
                Filter = new UserTaskFilter
                {
                    ProcessDefinitionId = ProcessDefinitionId.AssumeExists(processDefinitionId),
                    State = new UserTaskStateFilterProperty
                    {
                        Eq = UserTaskStateEnum.CREATED
                    }
                },
                Page = page,
            });
        }
        catch (Camunda.Orchestration.Sdk.HttpSdkException ex) when (ex.Status >= 500)
        {
            searchCursors.TryRemove(processDefinitionId, out _);
            logger.LogWarning(
                "User task search failed for {ProcessDefinitionId}; resetting its search cursor",
                processDefinitionId);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            return 0;
        }

        var handled = 0;

        try
        {
            await Task.WhenAll(response.Items.Select(async task =>
            {
                var taskKey = task.UserTaskKey.ToString();
                if (recentlyHandledTasks.ContainsKey(taskKey))
                    return;

                await completionSlots.WaitAsync(ct);
                try
                {
                    await Task.Delay(15, ct);

                    await client.CompleteUserTaskAsync(
                        task.UserTaskKey,
                        new UserTaskCompletionRequest(),
                        ct);
                    RememberHandledTask(taskKey);
                    Interlocked.Increment(ref handled);
                }
                catch (Camunda.Orchestration.Sdk.HttpSdkException ex) when (ex.Status is 404 or 409)
                {
                    // Secondary storage may still expose a task that was already completed.
                    RememberHandledTask(taskKey);
                }
                finally
                {
                    completionSlots.Release();
                }
            }));

            if (response.Page.HasMoreTotalItems && response.Page.EndCursor is { } endCursor)
                searchCursors[processDefinitionId] = endCursor;
            else
                searchCursors.TryRemove(processDefinitionId, out _);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // Another poll or application instance completed this task first.
        }
        catch (Camunda.Orchestration.Sdk.HttpSdkException ex) when (ex.IsBackpressure)
        {
            await Task.Delay(2000);
        }

        return handled;
    }

    private void RememberHandledTask(string taskKey)
    {
        recentlyHandledTasks[taskKey] = DateTimeOffset.UtcNow.Add(HandledTaskRetention);
    }

    private void RemoveExpiredHandledTasks()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var task in recentlyHandledTasks)
        {
            if (task.Value <= now)
                recentlyHandledTasks.TryRemove(task.Key, out _);
        }
    }
}
