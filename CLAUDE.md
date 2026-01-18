# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build entire solution
dotnet build CamundaStartup.sln

# Run the Aspire AppHost (starts all services including Camunda containers)
dotnet run --project Demo/CamundaStartup.Aspire.Hosting.Camunda.AppHost

# Run with specific secondary storage (postgres, sqlserver, or elastic)
dotnet run --project Demo/CamundaStartup.Aspire.Hosting.Camunda.AppHost -- --secondaryStorage postgres
```

**Prerequisites:** Docker Desktop must be running for container services.

## Project Structure

```
CamundaStartup/
├── Camunda.Client/                    # Core client library (gRPC + REST)
├── CamundaStartup.Aspire.Hosting.Camunda/  # Aspire hosting extensions
└── Demo/
    ├── Camunda.Startup.DemoApp/       # Sample web API with weather forecast workflow
    ├── CamundaStartup.Aspire.Hosting.Camunda.AppHost/  # Aspire orchestration entry point
    └── CamundaStartup.ServiceDefaults/  # Shared OpenTelemetry & resilience config
```

## Architecture Overview

This is a .NET 10 / Aspire 13 solution for integrating Camunda 8 workflow automation into .NET applications.

### Projects

| Project                                            | Description                                                          |
|----------------------------------------------------|----------------------------------------------------------------------|
| **Camunda.Client**                                 | Core client library - gRPC/REST clients, job workers, message publishing |
| **CamundaStartup.Aspire.Hosting.Camunda**          | Aspire extensions - `AddCamunda()`, storage backends, S3 backup      |
| **Camunda.Startup.DemoApp**                        | Sample web API demonstrating weather forecast workflow               |
| **CamundaStartup.Aspire.Hosting.Camunda.AppHost**  | Aspire orchestration host - wires up all containers and services     |
| **CamundaStartup.ServiceDefaults**                 | Shared configuration - OpenTelemetry, resilience, service discovery  |

### Demo Application Flow

```
POST /weatherforecast/{date}
  → IMessageClient.Publish() → Camunda (async) → Creates service task
  → Worker picks up task → JobHandler processes → Caches result
  → API returns 202 Accepted immediately

GET /weatherforecast/{date}
  → Returns cached result from memory
```

### Key Patterns

**Job Worker Registration:**
```csharp
builder.Services.AddCamunda(
    options => options.Endpoint = connectionString,
    builder => builder.AddWorker<MyJobHandler>());
```

**Job Handler Implementation:**
```csharp
[JobWorker(Type = "my-task:1")]
public class MyJobHandler : IJobHandler
{
    public async Task Handle(IJobClient client, IJob job, CancellationToken ct)
    {
        var input = job.GetVariablesAsType<MyInput>();
        // Process job...
        // Auto-completes by default, or call client.CompleteJobCommand(job)
    }
}
```

**Message Publishing:**
```csharp
[CamundaMessage(Name = "Message_Name", TimeToLiveInMs = 60_000)]
public record MyMessage(string Data);

await messageClient.Publish(new MyMessage("data"), correlationKey);
```

### JobWorker Configuration Options

- `Type` - Job type matching BPMN service task
- `AutoComplete` - Auto-complete job after handler (default: true)
- `UseStream` - Use gRPC streaming vs REST polling
- `TimeoutInMs` - Job lock timeout
- `FetchVariables` - Specific variables to fetch (empty = all)
- `PollingDelayInMs` / `PollingRequestTimeoutInMs` - Polling behavior

### BPMN Deployment Pattern

Deploy workflow definitions on startup using a background service:
```csharp
public class DeployBPMNDefinitionService(ICamundaClientRest client) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var file = await File.ReadAllBytesAsync("my-workflow.bpmn", stoppingToken);
        using var stream = new MemoryStream(file, writable: false);
        await client.DeploymentsAsync([new FileParameter(stream, "my-workflow.bpmn")], string.Empty, stoppingToken);
    }
}
```

## Service Endpoints

When running via AppHost:

| Service            | Port  | Protocol |
|--------------------|-------|----------|
| DemoApp            | 7230  | HTTPS    |
| Camunda REST       | 8080  | HTTP     |
| Camunda gRPC       | 26500 | HTTP/2   |
| Camunda Management | 9600  | HTTP     |

## Testing with .http Files

Use JetBrains HTTP Client files in Rider:
- `Demo/Camunda.Startup.DemoApp/Camunda.Startup.DemoApp.http` - API testing
