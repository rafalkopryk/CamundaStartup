using CamundaStartup.Aspire.Hosting.Camunda;
using CamundaStartup.Aspire.Hosting.Camunda.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var keycloakPassword = builder.AddParameter("keycloakPassword", secret: true);
var keycloakUser = builder.AddParameter("keycloakUser", "demo");

var keycloak = builder.AddKeycloak(
        "keycloak",
        8080,
        keycloakUser,
        keycloakPassword)
    .WithDataVolume()
    .WithRealmImport("./Realms")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithOtlpExporter();

var keycloakHttp = keycloak.GetEndpoint("http");
var keycloakIssuerUri = ReferenceExpression.Create(
    $"http://host.containers.internal:{keycloakHttp.Property(EndpointProperty.Port)}/realms/camunda-platform");

var camunda = builder.AddCamunda("camunda", 8081)
    .WithDataVolume("Camunda")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithOidc(
        issuerUri: keycloakIssuerUri,
        clientId: "orchestration",
        clientSecret: "orchestration-secret",
        redirectUri: ReferenceExpression.Create($"http://localhost:8081/sso-callback"),
        groupsClaim: "$.realm_access.roles")
    .WaitFor(keycloak);

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
    .WithEnvironment("CAMUNDA_OAUTH_URL", "http://localhost:8080/realms/camunda-platform/protocol/openid-connect/token")
    .WithEnvironment("CAMUNDA_CLIENT_ID", "demoapp")
    .WithEnvironment("CAMUNDA_CLIENT_SECRET", "demoapp-secret")
    .WithEnvironment("CAMUNDA_TOKEN_AUDIENCE", "orchestration-api")
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
        .WithLifetime(ContainerLifetime.Persistent);

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
