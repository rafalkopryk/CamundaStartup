using System.Security.Claims;
using Camunda.Startup.DemoApp.Authorization;
using Camunda.Startup.DemoApp.Feature;
using ClaimsAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Xunit;

namespace Camunda.Startup.DemoApp.Tests;

public sealed class PermissionCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"permission-tests-{Guid.NewGuid():N}");

    public PermissionCatalogTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void MultipleRolesCombineTheirPermissions()
    {
        var catalog = LoadCatalog(
            """
            { "permissions": [
              { "key": "forecast.read", "name": "READ", "resourceType": "FORECAST" },
              { "key": "forecast.create", "name": "CREATE", "resourceType": "FORECAST" }
            ] }
            """,
            """
            { "roles": [
              { "name": "reader", "permissions": ["forecast.read"] },
              { "name": "writer", "permissions": ["forecast.create"] }
            ] }
            """);

        Assert.True(catalog.RoleHasPermission(["reader", "writer"], "forecast.read"));
        Assert.True(catalog.RoleHasPermission(["reader", "writer"], "forecast.create"));
        Assert.False(catalog.RoleHasPermission(["READER", "unknown"], "forecast.read"));
        Assert.False(catalog.RoleHasPermission(["reader"], "undefined"));
    }

    [Fact]
    public void UndefinedPermissionReferenceIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => LoadCatalog(
            """
            { "permissions": [] }
            """,
            """
            { "roles": [{ "name": "reader", "permissions": ["missing"] }] }
            """));

        Assert.Contains("undefined permission 'missing'", exception.Message);
    }

    [Fact]
    public void DuplicateKeysAndRolesAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => LoadCatalog(
            """
            { "permissions": [
              { "key": "same", "name": "READ", "resourceType": "A" },
              { "key": "same", "name": "READ", "resourceType": "B" }
            ] }
            """,
            """
            { "roles": [] }
            """));

        Assert.Throws<InvalidOperationException>(() => LoadCatalog(
            """
            { "permissions": [] }
            """,
            """
            { "roles": [
              { "name": "same", "permissions": [] },
              { "name": "same", "permissions": [] }
            ] }
            """));
    }

    [Fact]
    public async Task HandlerRequiresAuthenticationAndAGrantedRole()
    {
        var catalog = LoadCatalog(
            """
            { "permissions": [{ "key": "forecast.read", "name": "READ", "resourceType": "FORECAST" }] }
            """,
            """
            { "roles": [{ "name": "reader", "permissions": ["forecast.read"] }] }
            """);
        var resolver = new TokenRoleResolver(Options.Create(new TokenRoleMappingOptions
        {
            Mappings = [new("Reader", "reader", Roles: ["reader"])],
        }));
        var handler = new PermissionAuthorizationHandler(new PermissionEvaluator(resolver, catalog));
        var requirement = new PermissionRequirement("forecast.read");

        var anonymousContext = new AuthorizationHandlerContext(
            [requirement], new ClaimsPrincipal(new ClaimsIdentity()), null);
        await handler.HandleAsync(anonymousContext);
        Assert.False(anonymousContext.HasSucceeded);

        var deniedContext = CreateContext(requirement, "Role", "unknown");
        await handler.HandleAsync(deniedContext);
        Assert.False(deniedContext.HasSucceeded);

        var allowedContext = CreateContext(requirement, "Role", "reader");
        await handler.HandleAsync(allowedContext);
        Assert.True(allowedContext.HasSucceeded);
    }

    [Fact]
    public void CifMatchingSupportsJsonArrays()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("Cif", "[\"123\",\"456\"]"),
                new Claim("Xnuc", "EMP-1"),
            ],
            "test");
        var principal = new ClaimsPrincipal(identity);

        Assert.True(principal.HasCif("456"));
        Assert.False(principal.HasCif("789"));
        Assert.True(principal.HasXnuc("EMP-1"));
        Assert.False(principal.HasXnuc("EMP-2"));
    }

    [Fact]
    public async Task IdentifierPermissionExtensionsRequireMatchingClaims()
    {
        var authorizationService = new SuccessfulAuthorizationService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("Cif", "123"),
            new Claim("Xnuc", "EMP-1"),
        ], "test"));

        Assert.True((await authorizationService.AuthorizeCifPermissionAsync(
            principal, "application.read-own", "123")).Succeeded);
        Assert.False((await authorizationService.AuthorizeCifPermissionAsync(
            principal, "application.read-own", "456")).Succeeded);
        Assert.True((await authorizationService.AuthorizeXnucPermissionAsync(
            principal, "application.read-own", "EMP-1")).Succeeded);
        Assert.False((await authorizationService.AuthorizeXnucPermissionAsync(
            principal, "application.read-own", "EMP-2")).Succeeded);
    }

    [Fact]
    public async Task LoanApplicationOperationHandlerCentralizesAllOrOwnRules()
    {
        var catalog = LoadCatalog(
            """
            { "permissions": [
              { "key": "application.read-own", "name": "READ", "resourceType": "APPLICATION" },
              { "key": "application.read-all", "name": "READ", "resourceType": "APPLICATION" }
            ] }
            """,
            """
            { "roles": [
              { "name": "Client", "permissions": ["application.read-own"] },
              { "name": "Analyst", "permissions": ["application.read-all"] }
            ] }
            """);
        var resolver = new TokenRoleResolver(Options.Create(new TokenRoleMappingOptions
        {
            Mappings =
            [
                new("Client", "Client", RequireCif: true),
                new("Analyst", "Analyst", Roles: ["Analyst"]),
            ],
        }));
        var handler = new LoanApplicationAuthorizationHandler(new PermissionEvaluator(resolver, catalog));
        var application = new LoanApplication(Guid.NewGuid(), "123", 1000m, "Submitted");

        var owner = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Cif", "123")], "test"));
        var ownerContext = new AuthorizationHandlerContext(
            [LoanApplicationOperations.Read], owner, application);
        await handler.HandleAsync(ownerContext);
        Assert.True(ownerContext.HasSucceeded);

        var otherClient = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Cif", "456")], "test"));
        var deniedContext = new AuthorizationHandlerContext(
            [LoanApplicationOperations.Read], otherClient, application);
        await handler.HandleAsync(deniedContext);
        Assert.False(deniedContext.HasSucceeded);

        var analyst = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Role", "Analyst")], "test"));
        var analystContext = new AuthorizationHandlerContext(
            [LoanApplicationOperations.Read], analyst, application);
        await handler.HandleAsync(analystContext);
        Assert.True(analystContext.HasSucceeded);
    }

    [Fact]
    public void TokenClaimsAreMappedToNormalizedRoles()
    {
        var options = Options.Create(new TokenRoleMappingOptions
        {
            Mappings =
            [
                new("TechnicalServices", "TechnicalService", ClientIds: ["ServiceA", "ServiceB"]),
                new("Client", "Client", RequireCif: true),
                new("AnalystScopes", "Analyst", Scopes: ["retail-credit:analyst", "milledesk:anl"]),
                new("AuditorRole", "Auditor", Roles: ["auditor"]),
                new("ReviewerRole", "Reviewer", Roles: ["reviewer"]),
            ],
        });
        var resolver = new TokenRoleResolver(options);
        var identity = new ClaimsIdentity(
        [
            new Claim("client_id", "ServiceA"),
            new Claim("Cif", "[\"*\",\"123\"]"),
            new Claim("scope", "openid retail-credit:analyst"),
            new Claim("Role", "auditor"),
            new Claim("Roles", "[\"reviewer\",\"other\"]"),
        ], "test");

        var roles = resolver.Resolve(new ClaimsPrincipal(identity));

        Assert.Equal(
            new HashSet<string> { "TechnicalService", "Client", "Analyst", "Auditor", "Reviewer" },
            roles);
    }

    [Fact]
    public void MappedRolesResolveToCatalogPermissions()
    {
        var catalog = LoadCatalog(
            """
            { "permissions": [
              { "key": "forecast.read", "name": "READ", "resourceType": "FORECAST" }
            ] }
            """,
            """
            { "roles": [{ "name": "Analyst", "permissions": ["forecast.read"] }] }
            """);
        var options = Options.Create(new TokenRoleMappingOptions
        {
            Mappings = [new("Analyst", "Analyst", Scopes: ["retail-credit:analyst"])],
        });
        var resolver = new TokenRoleResolver(options);
        var identity = new ClaimsIdentity(
            [new Claim("scope", "retail-credit:analyst")],
            "test");

        var roles = resolver.Resolve(new ClaimsPrincipal(identity));
        var permissions = catalog.GetPermissions(roles);

        Assert.Contains("forecast.read", permissions);
    }

    [Fact]
    public void WildcardsDoNotMapClientOrEmployerRoles()
    {
        var options = Options.Create(new TokenRoleMappingOptions
        {
            Mappings =
            [
                new("Client", "Client", RequireCif: true),
            ],
        });
        var resolver = new TokenRoleResolver(options);
        var identity = new ClaimsIdentity(
        [
            new Claim("Cif", "[\"*\"]"),
        ], "test");

        var roles = resolver.Resolve(new ClaimsPrincipal(identity));

        Assert.Empty(roles);
    }

    [Fact]
    public void ClientSpecificCifMappingOverridesDefaultClientRole()
    {
        var options = Options.Create(new TokenRoleMappingOptions
        {
            Mappings =
            [
                new("ClientPOS", "ClientPos", ClientIds: ["POS"], RequireCif: true,
                    ExclusiveGroup: "ClientType", Priority: 100),
                new("Client", "Client", RequireCif: true,
                    ExclusiveGroup: "ClientType"),
            ],
        });
        var resolver = new TokenRoleResolver(options);
        var identity = new ClaimsIdentity(
        [
            new Claim("client_id", "POS"),
            new Claim("Cif", "123"),
        ], "test");

        var roles = resolver.Resolve(new ClaimsPrincipal(identity));

        Assert.Equal("ClientPos", Assert.Single(roles));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private PermissionCatalog LoadCatalog(string permissions, string roles)
    {
        var permissionsPath = Path.Combine(_directory, $"permissions-{Guid.NewGuid():N}.json");
        var rolesPath = Path.Combine(_directory, $"roles-{Guid.NewGuid():N}.json");
        File.WriteAllText(permissionsPath, permissions);
        File.WriteAllText(rolesPath, roles);
        return PermissionCatalog.Load(permissionsPath, rolesPath);
    }

    private static AuthorizationHandlerContext CreateContext(
        PermissionRequirement requirement,
        string claimType,
        string claimValue)
    {
        var identity = new ClaimsIdentity([new Claim(claimType, claimValue)], "test");
        return new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(identity), null);
    }

    private sealed class SuccessfulAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(AuthorizationResult.Success());
    }
}
