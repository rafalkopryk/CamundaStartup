using Camunda.Orchestration.Sdk;
using Camunda.Startup.DemoApp.Dtos;
using Camunda.Startup.DemoApp.Feature;
using Camunda.Client.Extensions;
using Camunda.Startup.DemoApp.Authorization;
using Camunda.Startup.DemoApp.UseCases;
using ClaimsAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ILoanApplicationRepository, InMemoryLoanApplicationRepository>();
builder.Services.AddSingleton<IAuthorizationHandler, LoanApplicationAuthorizationHandler>();

builder.Services.AddClaimsAuthorization(
    Path.Combine(builder.Environment.ContentRootPath, "Authorization", "permissions.json"),
    Path.Combine(builder.Environment.ContentRootPath, "Authorization", "roles.json"));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "name";
    });

builder.Services.AddAuthorization();

builder.Services.AddHostedService<DeployBPMNDefinitionService>();

builder.Services.AddCamundaClient(options =>
{
    options.Config = new()
    {
       ["CAMUNDA_REST_ADDRESS"] = builder.Configuration.GetConnectionString("camunda") ?? string.Empty,
    };
});

builder.AddCamundaWorkers();

var app = builder.Build();

app.CreateJobWorker<RetrieveWeatherForecastJobHandler>(new JobWorkerConfig
{
    JobType = "weather-forecast-retrieve:1",
    JobTimeoutMs = 30_000,
    PollTimeoutMs = 10_000,
    MaxConcurrentJobs = 4,
    PollIntervalMs = 250,
});

app.CreateJobWorker<SendNotificationJobHandler>(new JobWorkerConfig
{
    JobType = "send-notification:1",
    JobTimeoutMs = 30_000,
    PollTimeoutMs = 10_000,
    MaxConcurrentJobs = 4,
    PollIntervalMs = 250,
});

app.MapDefaultEndpoints();

app.MapOpenApi();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapLoanApplicationEndpoints();

app.MapPost("/weatherforecast/{requestedDate}", async ([FromRoute] DateOnly requestedDate, CamundaClient messageClient) =>
    {
        await messageClient.PublishMessageAsync(new MessagePublicationRequest
        {
            Name = "Message_WeatherForecastRequestReceived",
            Variables = new WeatherForecastRequestReceived(requestedDate),
            MessageId = Guid.CreateVersion7().ToString(),
            TimeToLive = 60_000,
        });

    return TypedResults.Accepted(string.Empty);
})
.RequirePermission(PermissionKeys.WeatherForecastCreate)
.WithName("StartWeatherForecast");

app.MapGet("/weatherforecast/{requestedDate}", IResult ([FromRoute] DateOnly requestedDate, IMemoryCache memoryCache) =>
{
    return memoryCache.TryGetValue<WeatherForecast>($"WeatherForecast-{requestedDate:yyyy-MM-dd}", out var outValue)
        ? TypedResults.Ok(outValue)
        : TypedResults.NotFound();
})
.RequirePermission(PermissionKeys.WeatherForecastRead)
.WithName("GetWeatherForecast");

app.Run();

public record WeatherForecastRequestReceived(DateOnly RequestedDate);

public partial class Program;
