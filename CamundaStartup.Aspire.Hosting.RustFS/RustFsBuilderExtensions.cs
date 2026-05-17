using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace CamundaStartup.Aspire.Hosting.RustFS;

public static class RustFsBuilderExtensions
{
    private const int DefaultS3Port = 9000;
    private const int DefaultConsolePort = 9001;
    private const string DefaultRootCredential = "rustfsadmin";

    public static IResourceBuilder<RustFsResource> AddRustFs(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int? s3Port = null,
        int? consolePort = null,
        IResourceBuilder<ParameterResource>? accessKey = null,
        IResourceBuilder<ParameterResource>? secretKey = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);

        var accessKeyParameter = accessKey?.Resource
            ?? builder.AddParameter($"{name}-access-key", DefaultRootCredential).Resource;
        var secretKeyParameter = secretKey?.Resource
            ?? builder.AddParameter($"{name}-secret-key", DefaultRootCredential, secret: true).Resource;

        var resource = new RustFsResource(name, accessKeyParameter, secretKeyParameter);

        return builder
            .AddResource(resource)
            .WithImage(RustFsContainerImageTags.Image, RustFsContainerImageTags.Tag)
            .WithHttpEndpoint(port: s3Port, targetPort: DefaultS3Port, name: RustFsResource.S3EndpointName)
            .WithHttpEndpoint(port: consolePort, targetPort: DefaultConsolePort, name: RustFsResource.ConsoleEndpointName)
            .WithEnvironment("RUSTFS_ACCESS_KEY", accessKeyParameter)
            .WithEnvironment("RUSTFS_SECRET_KEY", secretKeyParameter)
            .WithEnvironment("RUSTFS_ADDRESS", ":" + DefaultS3Port)
            .WithEnvironment("RUSTFS_CONSOLE_ENABLE", "true");
    }

    public static IResourceBuilder<RustFsResource> WithDataVolume(
        this IResourceBuilder<RustFsResource> builder,
        string? name = null,
        bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(name, "/data", isReadOnly);
    }

    public static IResourceBuilder<ContainerResource> AddBucket(
        this IResourceBuilder<RustFsResource> rustfs,
        string bucketName)
    {
        ArgumentNullException.ThrowIfNull(rustfs);
        ArgumentNullException.ThrowIfNull(bucketName);

        var initName = $"{rustfs.Resource.Name}-init-{bucketName}";

        var mcHostValue = ReferenceExpression.Create(
            $"http://{rustfs.Resource.AccessKeyParameter}:{rustfs.Resource.SecretKeyParameter}@host.docker.internal:{rustfs.Resource.S3Endpoint.Property(EndpointProperty.Port)}");

        return rustfs.ApplicationBuilder
            .AddContainer(initName, "minio/mc", "latest")
            .WithEnvironment("MC_HOST_rustfs", mcHostValue)
            .WithEntrypoint("/bin/sh")
            .WithArgs(
                "-c",
                $"until mc mb --ignore-existing rustfs/{bucketName}; do echo 'waiting for rustfs...'; sleep 1; done")
            .WaitFor(rustfs);
    }
}
