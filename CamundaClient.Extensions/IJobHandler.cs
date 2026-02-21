using Camunda.Orchestration.Sdk.Runtime;

namespace Camunda.Client.Extensions;

public interface IJobHandler
{
    Task HandleAsync(ActivatedJob job, CancellationToken ct);
}

public interface IJobHandlerWithResult
{
    Task<object?> HandleAsync(ActivatedJob job, CancellationToken ct);
}
