using CamundaStartup.Aspire.Hosting.Camunda;
using CamundaStartup.Aspire.Hosting.Camunda.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// var minio = builder.AddMinioContainer("minio")
//     .WithDataVolume("minio")
//     .WithLifetime(ContainerLifetime.Persistent);

var secondaryStorageParameter = builder.AddParameter("secondaryStorage");
var secondaryStorage = (await secondaryStorageParameter.Resource.GetValueAsync(CancellationToken.None)) switch
{
    "postgres" => AddPostgres(),
    "sqlserver" => AddSqlServer(),
    "h2" => AddH2(),
    _ => AddElastic(),
};

var camunda = builder.AddCamunda("camunda", 8080)
    .WithDataVolume("Camunda")
    .WithLifetime(ContainerLifetime.Persistent);

camunda = secondaryStorage switch
{
    SecondaryStorage.Postgres postgres => camunda.WithRdmbsDatabase(
        postgres.Database.Resource.JdbcConnectionString,
        postgres.Server.Resource.UserNameReference,
        postgres.Server.Resource.PasswordParameter),
    SecondaryStorage.SqlServer sqlServer => camunda.WithRdmbsDatabase(
        sqlServer.Database.Resource.JdbcConnectionString,
        sqlServer.Server.Resource.UserNameReference,
        sqlServer.Server.Resource.PasswordParameter),
    SecondaryStorage.Elasticsearch elasticsearch => camunda.WithElasticDatabase(elasticsearch.Server.Resource
        .GetConnectionStringExpressionWithoutCredentials()),
    SecondaryStorage.H2 h2 => camunda.WithRdmbsDatabase(
        h2.JdbcConnectionString,
        h2.UserNameReference,
        h2.PasswordParameter),
    _ => camunda
};

if (secondaryStorage.Resource is not null)
{
    camunda = camunda.WaitFor(secondaryStorage.Resource);
}
    // .WithS3Backup(minio.Resource.UriExpression, minio.Resource.RootUser, minio.Resource.PasswordParameter)
    //.WaitFor(minio);

var demoApp = builder.AddProject<Projects.Camunda_Startup_DemoApp>("DemoApp")
    .WithReference(camunda, "camunda")
    .WaitFor(camunda);

builder.Build().Run();

SecondaryStorage AddPostgres()
{
    var postgres = builder.AddPostgres("postgres")
        .WithDataVolume("postgres")
        .WithLifetime(ContainerLifetime.Persistent);
    
    var database = postgres.AddDatabase("camunda-database", "camunda");
    
    return new SecondaryStorage.Postgres(postgres, database);
}

SecondaryStorage AddSqlServer()
{
    var sqlServer = builder.AddSqlServer("sqlserver")
        .WithDataVolume("sqlserver")
        
        .WithLifetime(ContainerLifetime.Persistent);

    var database = sqlServer.AddDatabase("camunda-database", "camunda");

    return new SecondaryStorage.SqlServer(sqlServer, database);
}

SecondaryStorage AddElastic()
{
    var elastic = builder.AddElasticsearch("elasticsearch")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithDataVolume("elastic")
        .WithLifetime(ContainerLifetime.Persistent);

    return new SecondaryStorage.Elasticsearch(elastic);
}

SecondaryStorage AddH2()
{
    var jdbcUrl = ReferenceExpression.Create($"jdbc:h2:file:/usr/local/camunda/data/h2/camunda;DB_CLOSE_DELAY=-1;AUTO_SERVER=TRUE");
    var username = ReferenceExpression.Create($"sa");
    var password = builder.AddParameter("h2Password", "", secret: true);

    return new SecondaryStorage.H2(jdbcUrl, username, password.Resource);
}

public abstract record SecondaryStorage(IResourceBuilder<IResource>? Resource)
{
    public record Postgres(
        IResourceBuilder<PostgresServerResource> Server,
        IResourceBuilder<PostgresDatabaseResource> Database) : SecondaryStorage(Server);

    public record SqlServer(
        IResourceBuilder<SqlServerServerResource> Server,
        IResourceBuilder<SqlServerDatabaseResource> Database) : SecondaryStorage(Server);

    public record Elasticsearch(IResourceBuilder<ElasticsearchResource> Server) : SecondaryStorage(Server);

    public record H2(
        ReferenceExpression JdbcConnectionString,
        ReferenceExpression UserNameReference,
        ParameterResource PasswordParameter) : SecondaryStorage((IResourceBuilder<IResource>?)null);
}