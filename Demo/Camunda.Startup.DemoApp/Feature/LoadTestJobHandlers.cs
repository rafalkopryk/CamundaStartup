using Camunda.Client.Extensions;
using Camunda.Orchestration.Sdk;
using Microsoft.Extensions.Caching.Memory;

namespace Camunda.Startup.DemoApp.Feature;

/// <summary>
/// Buffers the message consumed by the receive task immediately following this job.
/// </summary>
public sealed class ReleaseForecastStageJobHandler(CamundaClient client) : IJobHandler
{
    public async Task HandleAsync(ActivatedJob job, CancellationToken ct)
    {
        var input = job.GetVariables<LoadTestState>()
            ?? throw new InvalidOperationException("The process is missing load-test variables.");

        await client.PublishMessageAsync(new MessagePublicationRequest
        {
            Name = "Message_ContinueWeatherForecast",
            CorrelationKey = input.InstanceId,
            MessageId = $"{input.InstanceId}:{job.ElementId}",
            TimeToLive = 300_000,
        }, ct);
    }
}

public sealed class CompleteForecastJobHandler(
    IMemoryCache memoryCache,
    LoadTestRegistry loadTests) : IJobHandler
{
    public Task HandleAsync(ActivatedJob job, CancellationToken ct)
    {
        var input = job.GetVariables<LoadTestState>()
            ?? throw new InvalidOperationException("The process is missing load-test variables.");
        memoryCache.Set($"WeatherForecastInstance-{input.InstanceId}", true, TimeSpan.FromHours(1));
        if (input.LoadTestId is not null)
            loadTests.MarkCompleted(input.LoadTestId, input.InstanceId);
        return Task.CompletedTask;
    }
}

public sealed record LoadTestState(string InstanceId, DateOnly RequestedDate, string? LoadTestId = null);
