using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace CamundaStartup.Aspire.Hosting.SeaweedFS;

public static class SeaweedFsBuilderExtensions
{
    private const int DefaultS3Port = 8333;
    private const int DefaultMasterPort = 9333;
    private const int DefaultFilerPort = 8888;
    private const string DefaultRootCredential = "seaweedadmin";

    // Uses the README's `weed mini` quick-start: AWS_* env vars enable auth,
    // S3_BUCKET (comma-separated) pre-creates buckets at startup so no init
    // container is required. See https://github.com/seaweedfs/seaweedfs#quick-start-for-s3-api-on-docker
    public static IResourceBuilder<SeaweedFsResource> AddSeaweedFs(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int? s3Port = null,
        int? masterPort = null,
        int? filerPort = null,
        IResourceBuilder<ParameterResource>? accessKey = null,
        IResourceBuilder<ParameterResource>? secretKey = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);

        var accessKeyParameter = accessKey?.Resource
            ?? builder.AddParameter($"{name}-access-key", DefaultRootCredential).Resource;
        var secretKeyParameter = secretKey?.Resource
            ?? builder.AddParameter($"{name}-secret-key", DefaultRootCredential, secret: true).Resource;

        var resource = new SeaweedFsResource(name, accessKeyParameter, secretKeyParameter);

        return builder
            .AddResource(resource)
            .WithImage(SeaweedFsContainerImageTags.Image, SeaweedFsContainerImageTags.Tag)
            .WithHttpEndpoint(port: s3Port, targetPort: DefaultS3Port, name: SeaweedFsResource.S3EndpointName)
            .WithHttpEndpoint(port: masterPort, targetPort: DefaultMasterPort, name: SeaweedFsResource.MasterEndpointName)
            .WithHttpEndpoint(port: filerPort, targetPort: DefaultFilerPort, name: SeaweedFsResource.FilerEndpointName)
            .WithUrlForEndpoint(SeaweedFsResource.MasterEndpointName, url => url.DisplayText = "Master UI")
            .WithUrlForEndpoint(SeaweedFsResource.FilerEndpointName, url =>
            {
                url.DisplayText = "Filer UI";
                url.Url = "/buckets/";
            })
            .WithEnvironment("AWS_ACCESS_KEY_ID", accessKeyParameter)
            .WithEnvironment("AWS_SECRET_ACCESS_KEY", secretKeyParameter)
            .WithEnvironment(ctx =>
            {
                if (resource.Buckets.Count > 0)
                {
                    ctx.EnvironmentVariables["S3_BUCKET"] = string.Join(",", resource.Buckets);
                }
            })
            .WithArgs("mini", "-dir=/data");
    }

    public static IResourceBuilder<SeaweedFsResource> WithDataVolume(
        this IResourceBuilder<SeaweedFsResource> builder,
        string? name = null,
        bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(name, "/data", isReadOnly);
    }

    public static IResourceBuilder<SeaweedFsResource> WithBucket(
        this IResourceBuilder<SeaweedFsResource> builder,
        string bucketName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(bucketName);

        builder.Resource.Buckets.Add(bucketName);
        return builder;
    }
}
