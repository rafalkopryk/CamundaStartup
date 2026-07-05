using System.Security.Claims;
using Duende.IdentityModel;
using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Test;

namespace Camunda.Startup.IdentityServer;

public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile { UserClaims = { JwtClaimTypes.Role } },
        new IdentityResources.Email(),
    ];

    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope("orchestration-api", "Camunda Orchestration API"),
    ];

    public static IEnumerable<ApiResource> ApiResources =>
    [
        new ApiResource("orchestration-api", "Camunda Orchestration API")
        {
            Scopes = { "orchestration-api" },
            UserClaims = { JwtClaimTypes.Role },
        }
    ];

    public static IEnumerable<Client> Clients =>
    [
        // Browser SSO / auth-code flow for the Operate webapp.
        new Client
        {
            
            ClientId = "camunda-webapp",
            ClientName = "Camunda Orchestration (Operate webapp login)",
            ClientSecrets = { new Secret("camunda-webapp-secret".Sha256()) },

            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            RequireConsent = false,
            AlwaysIncludeUserClaimsInIdToken = true,

            RedirectUris =
            {
                "http://localhost:8081/sso-callback",
            },
            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                IdentityServerConstants.StandardScopes.Email,
                "orchestration-api",
            },
            AccessTokenLifetime = 360,
        },

        // Machine-to-machine client for DemoApp service-to-Camunda calls.
        new Client
        {
            ClientId = "demoapp",
            ClientName = "DemoApp",
            ClientSecrets = { new Secret("demoapp-secret".Sha256()) },

            AllowedGrantTypes = GrantTypes.ClientCredentials,
            AllowedScopes = { "orchestration-api" },

            // Empty prefix so the role claim emits as "role" (not "client_role"),
            // matching the JSONPath Camunda is configured with.
            ClientClaimsPrefix = "",
            AlwaysSendClientClaims = true,
            // Emit role as a JSON array (["admin"]) so Camunda's groupsClaim JSONPath
            // ($.role) extracts a list, not a scalar string.
            // Claims = { new ClientClaim(JwtClaimTypes.Role, """["admin"]""", IdentityServerConstants.ClaimValueTypes.Json) },
            AccessTokenLifetime = 3600,
        },
        new Client
        {
            ClientId = "demoapp-v2",
            ClientName = "DemoAppV2",
            ClientSecrets = { new Secret("demoapp-secret".Sha256()) },

            AllowedGrantTypes = GrantTypes.ClientCredentials,
            AllowedScopes = { "orchestration-api" },

            // Empty prefix so the role claim emits as "role" (not "client_role"),
            // matching the JSONPath Camunda is configured with.
            ClientClaimsPrefix = "",
            AlwaysSendClientClaims = true,
            // Emit role as a JSON array (["admin"]) so Camunda's groupsClaim JSONPath
            // ($.role) extracts a list, not a scalar string.
            AccessTokenLifetime = 3600,
        },
    ];

    public static List<TestUser> Users =>
    [
        new TestUser
        {
            SubjectId = "demo",
            Username = "demo",
            Password = "demo",
            Claims =
            {
                new Claim(JwtClaimTypes.Name, "Demo User"),
                new Claim(JwtClaimTypes.GivenName, "Demo"),
                new Claim(JwtClaimTypes.FamilyName, "User"),
                new Claim(JwtClaimTypes.Email, "demo@camunda.local"),
                new Claim(JwtClaimTypes.EmailVerified, "true", ClaimValueTypes.Boolean),
                new Claim(JwtClaimTypes.PreferredUserName, "demo"),
                new Claim(JwtClaimTypes.Role, """["admin"]""", IdentityServerConstants.ClaimValueTypes.Json),
            },
        },
        new TestUser
        {
            SubjectId = "rafal",
            Username = "rafal",
            Password = "demo",
            Claims =
            {
                new Claim(JwtClaimTypes.Name, "Rafal User"),
                new Claim(JwtClaimTypes.GivenName, "Rafal"),
                new Claim(JwtClaimTypes.FamilyName, "User"),
                new Claim(JwtClaimTypes.Email, "rafal@camunda.local"),
                new Claim(JwtClaimTypes.EmailVerified, "true", ClaimValueTypes.Boolean),
                new Claim(JwtClaimTypes.PreferredUserName, "rafal"),
                new Claim(JwtClaimTypes.Role, """["readonly-user"]""", IdentityServerConstants.ClaimValueTypes.Json),
            },
        },
        new TestUser
        {
            SubjectId = "jan",
            Username = "jan",
            Password = "demo",
            Claims =
            {
                new Claim(JwtClaimTypes.Name, "Jan User"),
                new Claim(JwtClaimTypes.GivenName, "Jan"),
                new Claim(JwtClaimTypes.FamilyName, "User"),
                new Claim(JwtClaimTypes.Email, "jan@camunda.local"),
                new Claim(JwtClaimTypes.EmailVerified, "true", ClaimValueTypes.Boolean),
                new Claim(JwtClaimTypes.PreferredUserName, "jan"),
                new Claim(JwtClaimTypes.Role, """["process-operator"]""", IdentityServerConstants.ClaimValueTypes.Json),
            },
        },
    ];
}
