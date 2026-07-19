using ClaimsAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Collections.Concurrent;

namespace Camunda.Startup.DemoApp.Feature;

public static class LoanApplicationPermissions
{
    public const string ReadOwn = "application.read-own";
    public const string ReadAll = "application.read-all";
    public const string UpdateOwn = "application.update-own";
    public const string UpdateAll = "application.update-all";

}

public static class LoanApplicationOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
}

public sealed class LoanApplicationAuthorizationHandler(IPermissionEvaluator permissionEvaluator)
    : AuthorizationHandler<OperationAuthorizationRequirement, LoanApplication>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        LoanApplication resource)
    {
        var authorized = requirement.Name switch
        {
            nameof(LoanApplicationOperations.Read) =>
                HasAllOrOwnPermission(
                    context.User,
                    resource.ClientCif,
                    LoanApplicationPermissions.ReadAll,
                    LoanApplicationPermissions.ReadOwn),
            nameof(LoanApplicationOperations.Update) =>
                HasAllOrOwnPermission(
                    context.User,
                    resource.ClientCif,
                    LoanApplicationPermissions.UpdateAll,
                    LoanApplicationPermissions.UpdateOwn),
            _ => false,
        };

        if (authorized)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private bool HasAllOrOwnPermission(
        System.Security.Claims.ClaimsPrincipal user,
        string resourceCif,
        string allPermission,
        string ownPermission) =>
        permissionEvaluator.IsGranted(user, allPermission) ||
        (permissionEvaluator.IsGranted(user, ownPermission) && user.HasCif(resourceCif));
}

public sealed record LoanApplication(
    Guid Id,
    string ClientCif,
    decimal Amount,
    string Status);

public interface ILoanApplicationRepository
{
    Task<LoanApplication?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<LoanApplication> UpdateAmountAsync(
        LoanApplication application,
        decimal amount,
        CancellationToken cancellationToken);
}

public sealed class InMemoryLoanApplicationRepository : ILoanApplicationRepository
{
    private static readonly ConcurrentDictionary<Guid, LoanApplication> Applications =
        new(new Dictionary<Guid, LoanApplication>
        {
            [Guid.Parse("11111111-1111-1111-1111-111111111111")] = new(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "123",
                25_000m,
                "Submitted"),
            [Guid.Parse("22222222-2222-2222-2222-222222222222")] = new(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "456",
                80_000m,
                "UnderReview"),
        });

    public Task<LoanApplication?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        Applications.TryGetValue(id, out var application);
        return Task.FromResult(application);
    }

    public Task<LoanApplication> UpdateAmountAsync(
        LoanApplication application,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var updated = application with { Amount = amount };
        Applications[application.Id] = updated;
        return Task.FromResult(updated);
    }
}

public sealed record UpdateLoanApplicationAmount(decimal Amount);

public static class LoanApplicationEndpoints
{
    public static IEndpointRouteBuilder MapLoanApplicationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/applications/{applicationId:guid}",
                GetApplicationAsync)
            .RequireAuthorization()
            .WithName("GetLoanApplication");

        endpoints.MapPut(
                "/applications/{applicationId:guid}/amount",
                UpdateApplicationAmountAsync)
            .RequireAuthorization()
            .WithName("UpdateLoanApplicationAmount");

        return endpoints;
    }

    private static async Task<Results<Ok<LoanApplication>, NotFound, ForbidHttpResult>> GetApplicationAsync(
        Guid applicationId,
        ILoanApplicationRepository repository,
        IAuthorizationService authorizationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var application = await repository.FindAsync(applicationId, cancellationToken);
        if (application is null)
        {
            return TypedResults.NotFound();
        }

        var authorization = await authorizationService.AuthorizeAsync(
            httpContext.User,
            application,
            LoanApplicationOperations.Read);

        return authorization.Succeeded
            ? TypedResults.Ok(application)
            : TypedResults.Forbid();
    }

    private static async Task<Results<Ok<LoanApplication>, NotFound, ForbidHttpResult>> UpdateApplicationAmountAsync(
        Guid applicationId,
        UpdateLoanApplicationAmount request,
        ILoanApplicationRepository repository,
        IAuthorizationService authorizationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var application = await repository.FindAsync(applicationId, cancellationToken);
        if (application is null)
        {
            return TypedResults.NotFound();
        }

        var authorization = await authorizationService.AuthorizeAsync(
            httpContext.User,
            application,
            LoanApplicationOperations.Update);
        if (!authorization.Succeeded)
        {
            return TypedResults.Forbid();
        }

        // Business validation belongs in the domain model/service, after authorization.
        var updated = await repository.UpdateAmountAsync(application, request.Amount, cancellationToken);
        return TypedResults.Ok(updated);
    }

}
