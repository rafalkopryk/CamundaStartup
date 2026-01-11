using CamundaStartup.Aspire.Hosting.Camunda;
using CamundaStartup.Aspire.Hosting.Camunda.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var secondaryStorageParameter = builder.AddParameter("secondaryStorage");
var secondaryStorage = (await secondaryStorageParameter.Resource.GetValueAsync(CancellationToken.None)) switch
{
    "postgres" => AddPostgres(),
    "sqlserver" => AddSqlServer(),
    _ => AddElastic(),
};

var camunda = builder.AddCamunda("camunda", 8080)
    .WithDataVolume("camunda")
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
        .GetConnectionStringExpressionWithoutCredentials())
};

camunda = camunda.WaitFor(secondaryStorage.Resource);

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

public abstract record SecondaryStorage(IResourceBuilder<IResource> Resource)
{
    public record Postgres(
        IResourceBuilder<PostgresServerResource> Server,
        IResourceBuilder<PostgresDatabaseResource> Database) : SecondaryStorage(Server);

    public record SqlServer(
        IResourceBuilder<SqlServerServerResource> Server,
        IResourceBuilder<SqlServerDatabaseResource> Database) : SecondaryStorage(Server);

    public record Elasticsearch(IResourceBuilder<ElasticsearchResource> Server) : SecondaryStorage(Server);
}