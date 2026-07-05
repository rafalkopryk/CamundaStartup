using CamundaStartup.Aspire.Hosting.Camunda;
using CamundaStartup.Aspire.Hosting.Camunda.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var identity = builder.AddProject<Projects.Camunda_Startup_IdentityServer>("identity")
    .WithEndpoint("http", e =>
    {
        e.Port = 8080;
        e.TargetPort = 8080;
        e.IsProxied = false;
    })
    .WithExternalHttpEndpoints();

// Browser hits the host (localhost); Camunda's backend hits the same IdentityServer
// from inside its container via host.docker.internal. Configure the four endpoints
// explicitly per https://docs.camunda.io/docs/self-managed/components/orchestration-cluster/admin/special-oidc-cases/
// instead of issuer-uri, so each side gets the URL that resolves for it.
// The port is pinned to 8080 on the endpoint above; using EndpointProperty.Port here
// would resolve to Aspire's proxy port even with IsProxied=false.
const string browserBase = "http://localhost:8080";
const string backendBase = "http://host.docker.internal:8080";

var camunda = builder.AddCamunda("camunda", 8081)
    .WithDataVolume("Camunda")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithOidc(
        clientId: "camunda-webapp",
        clientSecret: "camunda-webapp-secret",
        redirectUri: ReferenceExpression.Create($"http://localhost:8081/sso-callback"),
        authorizationUri: ReferenceExpression.Create($"{browserBase}/connect/authorize"),
        tokenUri: ReferenceExpression.Create($"{backendBase}/connect/token"),
        jwkSetUri: ReferenceExpression.Create($"{backendBase}/.well-known/openid-configuration/jwks"),
        endSessionEndpointUri: ReferenceExpression.Create($"{browserBase}/connect/endsession"),
        // Duende emits the client identifier as the OAuth2-standard `client_id` claim
        // in access tokens, not the OIDC ID-token-only `azp` claim.
        clientIdClaim: "client_id",
        groupsClaim: "$.role",
        scope: ["openid", "profile", "email", "orchestration-api"])
    .WaitFor(identity);

var storageType = await builder.AddParameter("secondaryStorage").Resource.GetValueAsync(CancellationToken.None);
var dependency = storageType switch
{
    "postgres" => ConfigurePostgres(),
    "sqlserver" => ConfigureSqlServer(),
    "elastic" => ConfigureElastic(),
    _ => ConfigureH2(),
};

if (dependency is not null)
    camunda.WaitFor(dependency);

builder.AddProject<Projects.Camunda_Startup_DemoApp>("DemoApp")
    .WithReference(camunda, "camunda")
    // .WithEnvironment("CAMUNDA_REST_ADDRESS", camunda)
    // .WithEnvironment("CAMUNDA_AUTH_STRATEGY", "BASIC")
    // .WithEnvironment("CAMUNDA_BASIC_AUTH_USERNAME", "demo")
    // .WithEnvironment("CAMUNDA_BASIC_AUTH_PASSWORD", "demo")

    .WithEnvironment("CAMUNDA_AUTH_STRATEGY", "OAUTH")
    .WithEnvironment("CAMUNDA_OAUTH_URL", "http://localhost:8080/connect/token")
    .WithEnvironment("CAMUNDA_CLIENT_ID", "demoapp")
    .WithEnvironment("CAMUNDA_CLIENT_SECRET", "demoapp-secret")
    .WithEnvironment("CAMUNDA_TOKEN_AUDIENCE", "orchestration-api")
    .WithEnvironment("CAMUNDA_OAUTH_SCOPE", "orchestration-api")

    // .WithEnvironment("CAMUNDA_DEFAULT_TENANT_ID", "demoapp")
    .WaitFor(camunda);

builder.Build().Run();


IResourceBuilder<IResource> ConfigurePostgres()
{
    var postgres = builder.AddPostgres("postgres")
        .WithDataVolume("postgres")
        .WithLifetime(ContainerLifetime.Persistent)
        .WithPgAdmin();

    var database = postgres.AddDatabase("camunda-database", "camunda");

    camunda.WithRdmbsDatabase(
        database.Resource.JdbcConnectionString,
        postgres.Resource.UserNameReference,
        postgres.Resource.PasswordParameter);

    return postgres;
}

IResourceBuilder<IResource> ConfigureSqlServer()
{
    var sqlServer = builder.AddSqlServer("sqlserver")
        .WithDataVolume("sqlserver")
        .WithLifetime(ContainerLifetime.Persistent);

    var database = sqlServer.AddDatabase("camunda-database", "camunda");

    camunda.WithRdmbsDatabase(
        database.Resource.JdbcConnectionString,
        sqlServer.Resource.UserNameReference,
        sqlServer.Resource.PasswordParameter);

    return sqlServer;
}

IResourceBuilder<IResource> ConfigureElastic()
{
    var elastic = builder.AddElasticsearch("elasticsearch")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithDataVolume("elastic")
        .WithLifetime(ContainerLifetime.Persistent)
        .WithElasticvue();

    camunda.WithElasticDatabase(elastic.Resource.GetConnectionStringExpressionWithoutCredentials());

    return elastic;
}

IResourceBuilder<IResource>? ConfigureH2()
{
    var jdbcUrl = ReferenceExpression.Create($"jdbc:h2:file:/usr/local/camunda/data/h2/camunda;DB_CLOSE_DELAY=-1;AUTO_SERVER=TRUE");
    var username = ReferenceExpression.Create($"sa");
    var password = builder.AddParameter("h2Password", "", secret: true);

    camunda.WithRdmbsDatabase(jdbcUrl, username, password.Resource);

    return null;
}
