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
public record MyOutput(string Status) : IJobResult;

public class MyJobHandlerWithResult : IJobHandlerWithResult
{
    public Task<IJobResult> HandleAsync(ActivatedJob job, CancellationToken ct)
    {
        return Task.FromResult<IJobResult>(new MyOutput("done"));
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

// Register individual job workers using handler type resolved from DI
app.CreateJobWorker<MyJobHandler>(new JobWorkerConfig
{
    JobType = "my-task:1",
    JobTimeoutMs = 30_000,
});

// Or register with a raw async delegate
app.CreateJobWorker(new JobWorkerConfig
{
    JobType = "my-other-task:1",
    JobTimeoutMs = 30_000,
}, async (job, ct) =>
{
    // Handle job inline
});
```

Calls can be chained since both overloads return `IHost`.

### Key Features

- **DI-friendly** — handlers are resolved per job via `ActivatorUtilities.CreateInstance`, so constructor injection works
- **Scoped lifetime** — each job execution gets its own `IServiceScope`
- **Minimal setup** — `AddCamundaWorkers()` + `CreateJobWorker<T>()` is all you need
