using System.Collections.Concurrent;
using System.Net;
using Camunda.Orchestration.Sdk;

namespace Camunda.Startup.DemoApp.Feature;

/// <summary>
/// Simulates users during unattended load tests while retaining Camunda-managed user tasks.
/// Completes all created user tasks.
/// </summary>
public sealed class AutoCompleteManagedUserTasksService : BackgroundService
{
    private readonly CamundaClient client;
    private readonly ILogger<AutoCompleteManagedUserTasksService> logger;
    private readonly int completionConcurrency;
    private readonly ConcurrentDictionary<string, DateTimeOffset> recentlyHandledTasks = new();
    private EndCursor? searchCursor;

    private static readonly TimeSpan HandledTaskRetention = TimeSpan.FromMinutes(5);
    private const int PageSize = 50;

    public AutoCompleteManagedUserTasksService(
        IConfiguration configuration,
        CamundaClient client,
        ILogger<AutoCompleteManagedUserTasksService> logger)
    {
        completionConcurrency = configuration.GetValue("LoadTest:UserTaskCompletionConcurrency", 3);
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

                var completed = await CompleteCreatedTasks(stoppingToken);

                if (completed == 0)
                    await Task.Delay(5000, stoppingToken);
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

    private async Task<int> CompleteCreatedTasks(CancellationToken ct)
    {
        SearchQueryPageRequest page = searchCursor is { } cursor
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
            searchCursor = null;
            logger.LogWarning("User task search failed; resetting the search cursor");
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            return 0;
        }

        var handled = 0;

        try
        {
            await Parallel.ForEachAsync(
                response.Items,
                new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = completionConcurrency,
                },
                async (task, taskCancellationToken) =>
                {
                    var taskKey = task.UserTaskKey.ToString();
                    if (recentlyHandledTasks.ContainsKey(taskKey))
                        return;

                    await Task.Delay(15, taskCancellationToken);

                    try
                    {
                        await client.CompleteUserTaskAsync(
                            task.UserTaskKey,
                            new UserTaskCompletionRequest(),
                            taskCancellationToken);
                        RememberHandledTask(taskKey);
                        Interlocked.Increment(ref handled);
                    }
                    catch (Camunda.Orchestration.Sdk.HttpSdkException ex) when (ex.Status is 404 or 409)
                    {
                        // Secondary storage may still expose a task that was already completed.
                        RememberHandledTask(taskKey);
                    }
                });

            if (response.Page.HasMoreTotalItems && response.Page.EndCursor is { } endCursor)
                searchCursor = endCursor;
            else
                searchCursor = null;
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
