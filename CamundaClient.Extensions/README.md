# CamundaClient.Extensions

Lightweight extensions for integrating Camunda 8 job workers into .NET applications using `Camunda.Orchestration.Sdk`.

## Usage

### 1. Define a Job Handler

Implement `IJobHandler` for fire-and-forget tasks:

```csharp
using Camunda.Client.Extensions;
using Camunda.Orchestration.Sdk.Runtime;

public class MyJobHandler : IJobHandler
{
    public Task HandleAsync(ActivatedJob job, CancellationToken ct)
    {
        var input = job.GetVariables<MyInput>();
        // Process job...
        return Task.CompletedTask;
    }
}
```

Or `IJobHandlerWithResult` when the worker should return variables:

```csharp
public class MyJobHandlerWithResult : IJobHandlerWithResult
{
    public async Task<object?> HandleAsync(ActivatedJob job, CancellationToken ct)
    {
        return new { Status = "done" };
    }
}
```

### 2. Register Workers

```csharp
using Camunda.Client.Extensions;
using Camunda.Orchestration.Sdk.Runtime;

// Register the hosted service that runs all workers
builder.AddCamundaWorkers();

var app = builder.Build();

// Register individual job workers
var client = app.Services.GetRequiredService<CamundaClient>();
client.CreateJobWorker<MyJobHandler>(new JobWorkerConfig
{
    JobType = "my-task:1",
    JobTimeoutMs = 30_000,
}, app.Services);
```

### Key Features

- **DI-friendly** — handlers are resolved per job via `ActivatorUtilities.CreateInstance`, so constructor injection works
- **Scoped lifetime** — each job execution gets its own `IServiceScope`
- **Minimal setup** — `AddCamundaWorkers()` + `CreateJobWorker<T>()` is all you need
