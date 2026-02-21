using Camunda.Orchestration.Sdk.Runtime;

namespace Camunda.Client.Extensions;

public interface IJobHandler
{
    Task HandleAsync(ActivatedJob job, CancellationToken ct);
}

public interface IJobResult { }

public interface IJobHandlerWithResult
{
    Task<IJobResult> HandleAsync(ActivatedJob job, CancellationToken ct);
}
