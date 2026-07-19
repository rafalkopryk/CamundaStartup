using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClaimsAuthorization;

public static class ClaimsAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddClaimsAuthorization(
        this IServiceCollection services,
        string permissionsPath,
        string rolesPath)
    {
        var catalog = PermissionCatalog.Load(permissionsPath, rolesPath);
        var mappingOptions = TokenRoleMappingOptions.Load(rolesPath);
        ValidateMappings(mappingOptions);

        services.AddSingleton<IOptions<TokenRoleMappingOptions>>(Options.Create(mappingOptions));
        services.AddSingleton<IPermissionCatalog>(catalog);
        services.AddSingleton<ITokenRoleResolver, TokenRoleResolver>();
        services.AddSingleton<IPermissionEvaluator, PermissionEvaluator>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.Configure<AuthorizationOptions>(options =>
        {
            foreach (var permissionKey in catalog.PermissionKeys)
            {
                options.AddPolicy(PermissionPolicy.For(permissionKey), policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.AddRequirements(new PermissionRequirement(permissionKey));
                });
            }
        });
        return services;
    }

    private static void ValidateMappings(TokenRoleMappingOptions options)
    {
        var mappings = options.Mappings;
        if (mappings.Any(mapping =>
                string.IsNullOrWhiteSpace(mapping.Key) ||
                string.IsNullOrWhiteSpace(mapping.Role)))
        {
            throw new InvalidOperationException("Mapping keys and roles cannot be blank.");
        }

        if (mappings.Select(mapping => mapping.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != mappings.Count)
        {
            throw new InvalidOperationException("Mapping keys must be unique.");
        }
    }
}
