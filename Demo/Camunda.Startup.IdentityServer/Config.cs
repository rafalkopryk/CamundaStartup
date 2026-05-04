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
        },
        new ApiResource("orchestration", "Camunda Orchestration (audience alias)")
        {
            Scopes = { "orchestration-api" },
            UserClaims = { JwtClaimTypes.Role },
        },
    ];

    public static IEnumerable<Client> Clients =>
    [
        // Browser SSO / auth-code flow for the Operate webapp.
        new Client
        {
            ClientId = "orchestration",
            ClientName = "Camunda Orchestration (Operate webapp login)",
            ClientSecrets = { new Secret("orchestration-secret".Sha256()) },

            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = false,
            RequireConsent = false,
            AlwaysIncludeUserClaimsInIdToken = true,

            RedirectUris =
            {
                "http://localhost:8081/sso-callback",
                "http://localhost:8081/login/oauth2/code/oidcclient",
                "http://host.containers.internal:8081/sso-callback",
            },
            PostLogoutRedirectUris = { "http://localhost:8081/" },
            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                IdentityServerConstants.StandardScopes.Email,
                "orchestration-api",
            },
            AccessTokenLifetime = 3600,
        },

        // Machine-to-machine client for DemoApp service-to-Camunda calls.
        new Client
        {
            ClientId = "demoapp",
            ClientName = "DemoApp (M2M / app-integrations)",
            ClientSecrets = { new Secret("demoapp-secret".Sha256()) },

            AllowedGrantTypes = GrantTypes.ClientCredentials,
            AllowedScopes = { "orchestration-api" },

            // Empty prefix so the role claim emits as "role" (not "client_role"),
            // matching the JSONPath Camunda is configured with.
            ClientClaimsPrefix = "",
            AlwaysSendClientClaims = true,
            // Emit role as a JSON array (["admin"]) so Camunda's groupsClaim JSONPath
            // ($.role) extracts a list, not a scalar string.
            Claims = { new ClientClaim(JwtClaimTypes.Role, """["admin"]""", IdentityServerConstants.ClaimValueTypes.Json) },
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
                new Claim(JwtClaimTypes.Role, """["dev"]""", IdentityServerConstants.ClaimValueTypes.Json),
            },
        },
    ];
}
