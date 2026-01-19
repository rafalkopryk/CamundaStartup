# JobHandler Code Templates

## Basic JobHandler Template

```csharp
using Camunda.Client.Jobs;

namespace {Namespace};

[JobWorker(Type = "{task-type}")]
public class {ClassName}JobHandler : IJobHandler
{
    public async Task Handle(IJobClient client, IJob job, CancellationToken cancellationToken)
    {
        var input = job.GetVariablesAsType<{ClassName}Input>();

        // TODO: Implement business logic

        // Job auto-completes by default when AutoComplete = true (default)
    }
}

public record {ClassName}Input(/* Add properties matching process variables */);
```

## JobHandler with Dependency Injection

```csharp
using Camunda.Client.Jobs;
using Microsoft.Extensions.Logging;

namespace {Namespace};

[JobWorker(Type = "{task-type}")]
public class {ClassName}JobHandler(
    ILogger<{ClassName}JobHandler> logger,
    {IService} service) : IJobHandler
{
    public async Task Handle(IJobClient client, IJob job, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing {TaskType} job {JobKey}", job.Type, job.Key);

        var input = job.GetVariablesAsType<{ClassName}Input>();

        var result = await service.ProcessAsync(input, cancellationToken);

        // Optionally set output variables
        await client.CompleteJobCommand(job.Key)
            .Variables(new { result })
            .Send(cancellationToken);
    }
}

public record {ClassName}Input(/* Add properties */);
```

## JobHandler with Manual Completion

When you need to set output variables or have conditional completion:

```csharp
using Camunda.Client.Jobs;

namespace {Namespace};

[JobWorker(Type = "{task-type}", AutoComplete = false)]
public class {ClassName}JobHandler : IJobHandler
{
    public async Task Handle(IJobClient client, IJob job, CancellationToken cancellationToken)
    {
        var input = job.GetVariablesAsType<{ClassName}Input>();

        // Business logic...
        var output = new {ClassName}Output(/* ... */);

        await client.CompleteJobCommand(job.Key)
            .Variables(output)
            .Send(cancellationToken);
    }
}

public record {ClassName}Input(/* ... */);
public record {ClassName}Output(/* ... */);
```

## JobHandler with Error Handling

```csharp
using Camunda.Client.Jobs;
using Microsoft.Extensions.Logging;

namespace {Namespace};

[JobWorker(Type = "{task-type}", AutoComplete = false)]
public class {ClassName}JobHandler(ILogger<{ClassName}JobHandler> logger) : IJobHandler
{
    public async Task Handle(IJobClient client, IJob job, CancellationToken cancellationToken)
    {
        try
        {
            var input = job.GetVariablesAsType<{ClassName}Input>();

            // Business logic...

            await client.CompleteJobCommand(job.Key).Send(cancellationToken);
        }
        catch (BusinessException ex)
        {
            logger.LogWarning(ex, "Business error in job {JobKey}", job.Key);

            // Throw BPMN error (caught by error boundary events)
            await client.ThrowErrorCommand(job.Key)
                .ErrorCode("BUSINESS_ERROR")
                .ErrorMessage(ex.Message)
                .Send(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Technical error in job {JobKey}", job.Key);

            // Fail job (will retry based on configuration)
            await client.FailCommand(job.Key)
                .Retries(job.Retries - 1)
                .ErrorMessage(ex.Message)
                .Send(cancellationToken);
        }
    }
}

public record {ClassName}Input(/* ... */);
```

## JobHandler with Specific Variables

Fetch only the variables you need for better performance:

```csharp
using Camunda.Client.Jobs;

namespace {Namespace};

[JobWorker(Type = "{task-type}", FetchVariables = ["orderId", "customerId", "amount"])]
public class {ClassName}JobHandler : IJobHandler
{
    public async Task Handle(IJobClient client, IJob job, CancellationToken cancellationToken)
    {
        var input = job.GetVariablesAsType<{ClassName}Input>();

        // Process with only the specified variables
    }
}

public record {ClassName}Input(string OrderId, string CustomerId, decimal Amount);
```

## Naming Conventions

| BPMN Task Type | Class Name | File Name |
|----------------|------------|-----------|
| `send-email:1` | `SendEmailJobHandler` | `SendEmailJobHandler.cs` |
| `validate-order:1` | `ValidateOrderJobHandler` | `ValidateOrderJobHandler.cs` |
| `process-payment:2` | `ProcessPaymentJobHandler` | `ProcessPaymentJobHandler.cs` |

### Converting Task Type to Class Name

1. Remove version suffix (`:1`, `:2`, etc.)
2. Convert kebab-case to PascalCase
3. Append `JobHandler` suffix

Examples:
- `send-notification:1` -> `SendNotificationJobHandler`
- `calculate-shipping-cost:1` -> `CalculateShippingCostJobHandler`
- `validate-customer-data:2` -> `ValidateCustomerDataJobHandler`

## Worker Registration

### Single Worker
```csharp
builder.Services.AddCamunda(
    options => options.Endpoint = builder.Configuration.GetConnectionString("camunda"),
    builder => builder.AddWorker<{ClassName}JobHandler>(new JobWorkerConfiguration()));
```

### Multiple Workers
```csharp
var config = new JobWorkerConfiguration();
builder.Services.AddCamunda(
    options => options.Endpoint = builder.Configuration.GetConnectionString("camunda"),
    builder => builder
        .AddWorker<FirstJobHandler>(config)
        .AddWorker<SecondJobHandler>(config)
        .AddWorker<ThirdJobHandler>(config));
```

### Custom Configuration
```csharp
var customConfig = new JobWorkerConfiguration
{
    TimeoutInMs = 30_000,           // 30 second timeout
    AutoComplete = true,             // Auto-complete after handler
    PollingDelayInMs = 500,          // Poll every 500ms
    PollingMaxJobsToActivate = 10,   // Fetch 10 jobs at a time
    RetryBackOffInMs = [1000, 2000, 4000]  // Exponential backoff
};

builder.Services.AddCamunda(
    options => options.Endpoint = builder.Configuration.GetConnectionString("camunda"),
    builder => builder.AddWorker<{ClassName}JobHandler>(customConfig));
```