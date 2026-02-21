using System.Diagnostics;
using Camunda.Orchestration.Sdk;
using Camunda.Orchestration.Sdk.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Camunda.Client.Extensions;

public static class CamundaWorkerExtensions
{
    public static IHostApplicationBuilder AddCamundaWorkers(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHostedService<CamundaWorkerHostedService>();
        return builder;
    }

    public static Camunda.Orchestration.Sdk.CamundaClient CreateJobWorker<T>(
        this Camunda.Orchestration.Sdk.CamundaClient client,
        JobWorkerConfig config,
        IServiceProvider serviceProvider) where T : class
    {
        var handlerType = typeof(T);

        if (typeof(IJobHandlerWithResult).IsAssignableFrom(handlerType))
        {
            client.CreateJobWorker(config, async (job, ct) =>
            {
                using var activity = Diagnostics.StartActivity(job);
                try
                {
                    await using var scope = serviceProvider.CreateAsyncScope();
                    var handler = (IJobHandlerWithResult)ActivatorUtilities.CreateInstance(
                        scope.ServiceProvider, handlerType);
                    return (object?)await handler.HandleAsync(job, ct);
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity?.AddException(ex);
                    throw;
                }
            });
        }
        else if (typeof(IJobHandler).IsAssignableFrom(handlerType))
        {
            client.CreateJobWorker(config, async (job, ct) =>
            {
                using var activity = Diagnostics.StartActivity(job);
                try
                {
                    await using var scope = serviceProvider.CreateAsyncScope();
                    var handler = (IJobHandler)ActivatorUtilities.CreateInstance(
                        scope.ServiceProvider, handlerType);
                    await handler.HandleAsync(job, ct);
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity?.AddException(ex);
                    throw;
                }
            });
        }
        else
        {
            throw new InvalidOperationException(
                $"{handlerType.Name} must implement IJobHandler or IJobHandlerWithResult.");
        }

        return client;
    }

    public static IHost CreateJobWorker<T>(
        this IHost host,
        JobWorkerConfig config) where T : class
    {
        var client = host.Services.GetRequiredService<Camunda.Orchestration.Sdk.CamundaClient>();
        client.CreateJobWorker<T>(config, host.Services);
        return host;
    }

    public static IHost CreateJobWorker(
        this IHost host,
        JobWorkerConfig config,
        Func<ActivatedJob, CancellationToken, Task> handler)
    {
        var client = host.Services.GetRequiredService<Camunda.Orchestration.Sdk.CamundaClient>();
        client.CreateJobWorker(config, handler);
        return host;
    }
}

public class CamundaWorkerHostedService(
    Camunda.Orchestration.Sdk.CamundaClient client,
    ILogger<CamundaWorkerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting Camunda workers");
        await client.RunWorkersAsync(ct: stoppingToken);
    }
}
