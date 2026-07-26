using Camunda.Orchestration.Sdk;
using Camunda.Startup.DemoApp.Dtos;
using Camunda.Startup.DemoApp.Feature;
using Camunda.Client.Extensions;
using Camunda.Startup.DemoApp.UseCases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LoadTestRegistry>();

builder.Services.AddHostedService<DeployBPMNDefinitionService>();
builder.Services.AddHostedService<AutoCompleteManagedUserTasksService>();

builder.Services.AddCamundaClient(options =>
{
    options.Config = new()
    {
       ["CAMUNDA_REST_ADDRESS"] = builder.Configuration.GetConnectionString("camunda"),
    };
});

builder.AddCamundaWorkers();

var workerConcurrency = builder.Configuration.GetValue("LoadTest:WorkerConcurrency", 32);
var app = builder.Build();

app.CreateJobWorker<RetrieveWeatherForecastJobHandler>(new JobWorkerConfig
{
    JobType = "weather-forecast-retrieve:1",
    JobTimeoutMs = 30_000,
    PollTimeoutMs = 5_000,
    MaxConcurrentJobs = workerConcurrency,
    PollIntervalMs = 50,
});

app.CreateJobWorker<SendNotificationJobHandler>(new JobWorkerConfig
{
    JobType = "send-notification:1",
    JobTimeoutMs = 30_000,
    PollTimeoutMs = 5_000,
    MaxConcurrentJobs = workerConcurrency,
    PollIntervalMs = 50,
});

app.CreateJobWorker<ReleaseForecastStageJobHandler>(new JobWorkerConfig
{
    JobType = "weather-forecast-release-stage:1",
    JobTimeoutMs = 30_000,
    PollTimeoutMs = 5_000,
    MaxConcurrentJobs = workerConcurrency,
    PollIntervalMs = 50,
});

app.CreateJobWorker<CompleteForecastJobHandler>(new JobWorkerConfig
{
    JobType = "weather-forecast-complete:1",
    JobTimeoutMs = 30_000,
    PollTimeoutMs = 5_000,
    MaxConcurrentJobs = workerConcurrency,
    PollIntervalMs = 50,
});

app.MapDefaultEndpoints();

app.MapOpenApi();
app.UseHttpsRedirection();

app.MapPost("/jobs/weatherforecast/{requestedDate}", StartWeatherForecastJob)
.WithName("StartWeatherForecastJob");

app.MapPost("/weatherforecast/{requestedDate}", StartWeatherForecastJob)
.WithName("StartWeatherForecast");

static async Task<IResult> StartWeatherForecastJob(
    [FromRoute] DateOnly requestedDate,
    [FromQuery] string? loadTestId,
    CamundaClient camundaClient,
    LoadTestRegistry loadTests,
    CancellationToken cancellationToken,
    [FromQuery] int count = 1)
{
    if (count <= 0)
        return TypedResults.BadRequest("Count must be greater than zero.");

    var instanceIds = Enumerable.Range(0, count)
        .Select(_ => Guid.CreateVersion7().ToString())
        .ToArray();

    foreach (var instanceId in instanceIds)
    {
        await camundaClient.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = ProcessDefinitionId.AssumeExists("weather-forecast"),
            Variables = new WeatherForecastRequestReceived(instanceId, requestedDate, loadTestId),
        }, cancellationToken);
    }

    if (loadTestId is not null)
    {
        foreach (var instanceId in instanceIds)
            loadTests.MarkStarted(loadTestId, instanceId);
    }

    return TypedResults.Accepted(
        $"/weatherforecast/instances/{instanceIds[0]}",
        new WeatherForecastJobsAccepted(instanceIds[0], instanceIds));
}

app.MapGet("/load-tests/{loadTestId}", (
    [FromRoute] string loadTestId,
    LoadTestRegistry loadTests) => TypedResults.Ok(loadTests.GetStatus(loadTestId)))
.WithName("GetLoadTestStatus");

app.MapGet("/weatherforecast/instances/{instanceId}", IResult (
    [FromRoute] string instanceId,
    IMemoryCache memoryCache) =>
{
    return memoryCache.TryGetValue($"WeatherForecastInstance-{instanceId}", out _)
        ? TypedResults.Ok(new WeatherForecastInstanceStatus(instanceId, "completed"))
        : TypedResults.Accepted(string.Empty, new WeatherForecastInstanceStatus(instanceId, "running"));
})
.WithName("GetWeatherForecastInstance");

app.MapGet("/weatherforecast/{requestedDate}", IResult ([FromRoute] DateOnly requestedDate, IMemoryCache memoryCache) =>
{
    return memoryCache.TryGetValue<WeatherForecast>($"WeatherForecast-{requestedDate:yyyy-MM-dd}", out var outValue)
        ? TypedResults.Ok(outValue)
        : TypedResults.NotFound();
})
.WithName("GetWeatherForecast");

app.Run();

public record WeatherForecastRequestReceived(string InstanceId, DateOnly RequestedDate, string? LoadTestId = null);
public record WeatherForecastAccepted(string InstanceId);
public record WeatherForecastJobsAccepted(string InstanceId, IReadOnlyList<string> InstanceIds);
public record WeatherForecastInstanceStatus(string InstanceId, string Status);
