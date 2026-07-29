using CamundaStartup.Aspire.Hosting.Camunda;
using CamundaStartup.Aspire.Hosting.Camunda.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
const string ElasticStackVersion = "8.19.17";

var storageType = await builder.AddParameter("secondaryStorage").Resource.GetValueAsync(CancellationToken.None);
var camundaDataVolume  = storageType switch
{
    "postgres" => "camunda-postgres",
    "sqlserver" => "camunda",
    "elastic" => "camunda-elastic",
    _ => "camunda-h2",
};

var camunda = builder.AddCamunda("camunda", 8080)
    .WithDataVolume(camundaDataVolume)
    .WithLifetime(ContainerLifetime.Persistent);

var optimizeEnabledValue = await builder.AddParameter("optimizeEnabled").Resource.GetValueAsync(CancellationToken.None);
var optimizeEnabled = bool.TryParse(optimizeEnabledValue, out var enabled) && enabled;

IResourceBuilder<ElasticsearchResource>? elastic = null;
if (storageType == "elastic" || optimizeEnabled)
    elastic = ConfigureElasticsearch();

if (optimizeEnabled)
{
    ConfigureOptimize(elastic!);
    camunda.WithElasticExporter(elastic!.Resource.GetConnectionStringExpressionWithoutCredentials());
    camunda.WaitFor(elastic);
}

var dependency = storageType switch
{
    "postgres" => ConfigurePostgres(),
    "sqlserver" => ConfigureSqlServer(),
    "elastic" => ConfigureElasticSecondaryStorage(elastic!),
    _ => ConfigureH2(),
};

if (dependency is not null)
    camunda.WaitFor(dependency);

builder.AddProject<Projects.Camunda_Startup_DemoApp>("DemoApp")
    .WithReference(camunda, "camunda")
    .WithEnvironment("CAMUNDA_SDK_BACKPRESSURE_PROFILE", "CONSERVATIVE")
    .WithEnvironment("CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS", "6")
    .WithEnvironment("CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS", "250")
    .WithEnvironment("CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS", "5000")
    .WaitFor(camunda);

builder.Build().Run();

return;

IResourceBuilder<IResource> ConfigurePostgres()
{
    var postgres = builder.AddPostgres("postgres")
        .WithDataVolume("camunda-postgres-db")
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

IResourceBuilder<IResource> ConfigureElasticSecondaryStorage(
    IResourceBuilder<ElasticsearchResource> elastic)
{
    camunda.WithElasticDatabase(elastic.Resource.GetConnectionStringExpressionWithoutCredentials());
    return elastic;
}

IResourceBuilder<ElasticsearchResource> ConfigureElasticsearch()
{
    var elastic = builder.AddElasticsearch("elasticsearch")
        .WithImageTag(ElasticStackVersion)
        .WithEnvironment("xpack.security.enabled", "false")
        .WithDataVolume("elastic")
        .WithLifetime(ContainerLifetime.Persistent);

    builder.AddContainer("kibana", "docker.elastic.co/kibana/kibana", ElasticStackVersion)
        .WithEnvironment("ELASTICSEARCH_HOSTS", elastic.Resource.GetKibanaHostsExpression())
        .WithHttpEndpoint(targetPort: 5601, name: "http")
        .WithExternalHttpEndpoints()
        .WithLifetime(ContainerLifetime.Persistent)
        .WaitFor(elastic);

    return elastic;
}

void ConfigureOptimize(IResourceBuilder<ElasticsearchResource> elastic)
{
    var identityPostgres = builder.AddContainer("identity-postgres", "postgres", "15-alpine3.22")
        .WithEnvironment("POSTGRES_DB", "keycloak")
        .WithEnvironment("POSTGRES_USER", "keycloak")
        .WithEnvironment("POSTGRES_PASSWORD", "demo-postgres-password")
        .WithEndpoint(targetPort: 5432, name: "tcp")
        .WithVolume("identity-postgres", "/var/lib/postgresql/data")
        .WithLifetime(ContainerLifetime.Persistent);

    var keycloak = builder.AddContainer("keycloak", "camunda/keycloak", "quay-26.6.4")
        .WithArgs("start")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
        .WithEnvironment("KC_DB", "postgres")
        .WithEnvironment(
            "KC_DB_URL",
            ReferenceExpression.Create(
                $"jdbc:postgresql://{identityPostgres.Resource.GetEndpoint("tcp").Property(EndpointProperty.Host)}:{identityPostgres.Resource.GetEndpoint("tcp").Property(EndpointProperty.Port)}/keycloak"))
        .WithEnvironment("KC_DB_USERNAME", "keycloak")
        .WithEnvironment("KC_DB_PASSWORD", "demo-postgres-password")
        .WithEnvironment("KC_HTTP_ENABLED", "true")
        .WithEnvironment("KC_HTTP_PORT", "18080")
        .WithEnvironment("KC_HTTP_RELATIVE_PATH", "/auth")
        .WithEnvironment("KC_HEALTH_ENABLED", "true")
        .WithEnvironment("KC_HOSTNAME_STRICT", "false")
        .WithEnvironment("KC_TRANSACTION_XA_ENABLED", "false")
        .WithHttpEndpoint(port: 18080, targetPort: 18080, name: "http")
        .WithExternalHttpEndpoints()
        .WithLifetime(ContainerLifetime.Persistent)
        .WaitFor(identityPostgres);

    var identity = builder.AddContainer("identity", "camunda/identity", "8.9.6")
        .WithEnvironment("IDENTITY_DATABASE_HOST", identityPostgres.Resource.GetEndpoint("tcp").Property(EndpointProperty.Host))
        .WithEnvironment("IDENTITY_DATABASE_PORT", identityPostgres.Resource.GetEndpoint("tcp").Property(EndpointProperty.Port))
        .WithEnvironment("IDENTITY_DATABASE_NAME", "keycloak")
        .WithEnvironment("IDENTITY_DATABASE_USERNAME", "keycloak")
        .WithEnvironment("IDENTITY_DATABASE_PASSWORD", "demo-postgres-password")
        .WithEnvironment("VALUES_KEYCLOAK_INIT_OPTIMIZE_SECRET", "demo-optimize-secret")
        .WithEnvironment("KEYCLOAK_ADMIN_USER", "admin")
        .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", "admin")
        .WithEnvironment("HOST", "localhost")
        .WithEnvironment("KEYCLOAK_HOST", keycloak.Resource.GetEndpoint("http").Property(EndpointProperty.Host))
        .WithEnvironment("RESOURCE_PERMISSIONS_ENABLED", "false")
        .WithBindMount(
            Path.Combine(builder.AppHostDirectory, ".identity", "application.yaml"),
            "/app/application.yaml",
            isReadOnly: true)
        .WithHttpEndpoint(port: 8084, targetPort: 8084, name: "http")
        .WithHttpEndpoint(targetPort: 8082, name: "management")
        .WithExternalHttpEndpoints()
        .WithHttpHealthCheck("/actuator/health", endpointName: "management")
        .WithLifetime(ContainerLifetime.Persistent)
        .WaitFor(keycloak);

    builder.AddContainer("optimize", "camunda/optimize", "8.9.9")
        .WithEnvironment(
            "OPTIMIZE_ELASTICSEARCH_HOST",
            elastic.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
        .WithEnvironment(
            "OPTIMIZE_ELASTICSEARCH_HTTP_PORT",
            elastic.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
        .WithEnvironment("SPRING_PROFILES_ACTIVE", "ccsm")
        .WithEnvironment("CAMUNDA_OPTIMIZE_ZEEBE_ENABLED", "true")
        .WithEnvironment("CAMUNDA_OPTIMIZE_ENTERPRISE", "false")
        .WithEnvironment("CAMUNDA_OPTIMIZE_SECURITY_AUTH_COOKIE_SAME_SITE_ENABLED", "false")
        .WithEnvironment("CAMUNDA_OPTIMIZE_UI_LOGOUT_HIDDEN", "true")
        .WithEnvironment("CAMUNDA_OPTIMIZE_IDENTITY_ISSUER_URL", "http://localhost:18080/auth/realms/camunda-platform")
        .WithEnvironment(
            "CAMUNDA_OPTIMIZE_IDENTITY_ISSUER_BACKEND_URL",
            ReferenceExpression.Create(
                $"http://{keycloak.Resource.GetEndpoint("http").Property(EndpointProperty.Host)}:{keycloak.Resource.GetEndpoint("http").Property(EndpointProperty.Port)}/auth/realms/camunda-platform"))
        .WithEnvironment("CAMUNDA_OPTIMIZE_IDENTITY_CLIENTID", "optimize")
        .WithEnvironment("CAMUNDA_OPTIMIZE_IDENTITY_CLIENTSECRET", "demo-optimize-secret")
        .WithEnvironment("CAMUNDA_OPTIMIZE_IDENTITY_AUDIENCE", "optimize-api")
        .WithEnvironment(
            "CAMUNDA_OPTIMIZE_IDENTITY_BASE_URL",
            ReferenceExpression.Create(
                $"http://{identity.Resource.GetEndpoint("http").Property(EndpointProperty.Host)}:{identity.Resource.GetEndpoint("http").Property(EndpointProperty.Port)}"))
        .WithEnvironment("CAMUNDA_OPTIMIZE_IDENTITY_REDIRECT_ROOT_URL", "http://localhost:8083")
        .WithHttpEndpoint(port: 8083, targetPort: 8090, name: "http")
        .WithExternalHttpEndpoints()
        .WithHttpHealthCheck("/api/readyz")
        .WithLifetime(ContainerLifetime.Persistent)
        .WaitFor(elastic)
        .WaitFor(identity);
}

IResourceBuilder<IResource>? ConfigureH2()
{
    var jdbcUrl = ReferenceExpression.Create($"jdbc:h2:file:/usr/local/camunda/data/h2/camunda;DB_CLOSE_DELAY=-1;AUTO_SERVER=TRUE");
    var username = ReferenceExpression.Create($"sa");
    var password = builder.AddParameter("h2Password", "", secret: true);

    camunda.WithRdmbsDatabase(jdbcUrl, username, password.Resource);

    return null;
}
