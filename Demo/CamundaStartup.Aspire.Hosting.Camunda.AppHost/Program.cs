using Aspire.Hosting.ApplicationModel;
using CamundaStartup.Aspire.Hosting.Camunda;
using CamundaStartup.Aspire.Hosting.Camunda.AppHost;
using CamundaStartup.Aspire.Hosting.RustFS;
using CamundaStartup.Aspire.Hosting.SeaweedFS;

var builder = DistributedApplication.CreateBuilder(args);

var backupStorage = await builder.AddParameter("backupStorage").Resource.GetValueAsync(CancellationToken.None);
var backup = backupStorage switch
{
    "rustfs" => ConfigureRustFs(),
    _ => ConfigureSeaweedFs(),
};

var camunda = builder.AddCamunda("camunda", 8081)
    .WithDataVolume("Camunda")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithS3Backup(
        endpoint: ReferenceExpression.Create($"http://host.docker.internal:{backup.S3Port}"),
        accessKey: backup.AccessKey,
        secretKey: backup.SecretKey)
    .WithScheduledBackup(
        schedule: "PT1M",
        retentionWindow: "PT5M",
        retentionCleanupSchedule: "PT1M",
        checkpointInterval: "PT15S")
    .WaitForCompletion(backup.BucketInit);

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
    .WaitFor(camunda);

builder.Build().Run();

return;

IResourceBuilder<IResource> ConfigurePostgres()
{
    var postgres = builder.AddPostgres("postgres")
        .WithDataVolume("postgres")
        .WithLifetime(ContainerLifetime.Persistent);

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

BackupBinding ConfigureRustFs()
{
    var rustfs = builder.AddRustFs("rustfs", s3Port: 9000, consolePort: 9001)
        .WithDataVolume("rustfs")
        .WithLifetime(ContainerLifetime.Persistent);

    var bucketInit = rustfs.AddBucket("camunda-backup");

    return new BackupBinding(
        S3Port: rustfs.Resource.S3Endpoint.Property(EndpointProperty.Port),
        AccessKey: rustfs.Resource.AccessKeyParameter,
        SecretKey: rustfs.Resource.SecretKeyParameter,
        BucketInit: bucketInit);
}

BackupBinding ConfigureSeaweedFs()
{
    var seaweedfs = builder.AddSeaweedFs("seaweedfs", s3Port: 8333, masterPort: 9333, filerPort: 8888)
        .WithDataVolume("seaweedfs")
        .WithLifetime(ContainerLifetime.Persistent);

    var bucketInit = seaweedfs.AddBucket("camunda-backup");

    return new BackupBinding(
        S3Port: seaweedfs.Resource.S3Endpoint.Property(EndpointProperty.Port),
        AccessKey: seaweedfs.Resource.AccessKeyParameter,
        SecretKey: seaweedfs.Resource.SecretKeyParameter,
        BucketInit: bucketInit);
}

internal record BackupBinding(
    EndpointReferenceExpression S3Port,
    ParameterResource AccessKey,
    ParameterResource SecretKey,
    IResourceBuilder<ContainerResource> BucketInit);
