using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using System.Security.Claims;

namespace ClaimsAuthorization;

public sealed record PermissionRequirement(string PermissionKey) : IAuthorizationRequirement;

public interface IPermissionEvaluator
{
    bool IsGranted(ClaimsPrincipal user, string permissionKey);
}

public sealed class PermissionEvaluator(
    ITokenRoleResolver roleResolver,
    IPermissionCatalog permissionCatalog) : IPermissionEvaluator
{
    public bool IsGranted(ClaimsPrincipal user, string permissionKey) =>
        user.Identity?.IsAuthenticated == true &&
        permissionCatalog.RoleHasPermission(roleResolver.Resolve(user), permissionKey);
}

public sealed class PermissionAuthorizationHandler(IPermissionEvaluator permissionEvaluator)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (!permissionEvaluator.IsGranted(context.User, requirement.PermissionKey))
        {
            return Task.CompletedTask;
        }

        context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

internal static class PermissionPolicy
{
    private const string Prefix = "Permission:";

    public static string For(string permissionKey) => $"{Prefix}{permissionKey}";
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permissionKey)
    {
        Policy = PermissionPolicy.For(permissionKey);
    }
}

public static class PermissionAuthorizationExtensions
{
    public static RouteHandlerBuilder RequirePermission(
        this RouteHandlerBuilder builder,
        string permissionKey)
    {
        builder.RequireAuthorization(PermissionPolicy.For(permissionKey));
        return builder;
    }

    public static Task<AuthorizationResult> AuthorizePermissionAsync(
        this IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        string permissionKey,
        object? resource = null) =>
        authorizationService.AuthorizeAsync(user, resource, PermissionPolicy.For(permissionKey));

    public static async Task<AuthorizationResult> AuthorizeCifPermissionAsync(
        this IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        string permissionKey,
        string cif)
    {
        var result = await authorizationService.AuthorizePermissionAsync(user, permissionKey);
        return result.Succeeded && user.HasCif(cif)
            ? result
            : AuthorizationResult.Failed();
    }

    public static async Task<AuthorizationResult> AuthorizeXnucPermissionAsync(
        this IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        string permissionKey,
        string xnuc)
    {
        var result = await authorizationService.AuthorizePermissionAsync(user, permissionKey);
        return result.Succeeded && user.HasXnuc(xnuc)
            ? result
            : AuthorizationResult.Failed();
    }

}
