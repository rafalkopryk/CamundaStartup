using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace CamundaStartup.Aspire.Hosting.SeaweedFS;

public static class SeaweedFsBuilderExtensions
{
    private const int DefaultS3Port = 8333;
    private const int DefaultMasterPort = 9333;
    private const int DefaultFilerPort = 8888;
    private const string DefaultRootCredential = "seaweedadmin";

    // SeaweedFS's S3 API rejects every signed request with InvalidAccessKeyId
    // unless an identities config (-s3.config) is mounted. We generate one at
    // /etc/seaweedfs/s3.json from the access/secret-key parameters so the same
    // credentials accepted by Camunda's WithS3Backup work end-to-end.
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
            .WithContainerFiles("/etc/seaweedfs", async (_, ct) =>
            {
                var ak = await accessKeyParameter.GetValueAsync(ct) ?? string.Empty;
                var sk = await secretKeyParameter.GetValueAsync(ct) ?? string.Empty;
                var json = $$"""
                {
                  "identities": [
                    {
                      "name": "admin",
                      "credentials": [
                        {
                          "accessKey": "{{ak}}",
                          "secretKey": "{{sk}}"
                        }
                      ],
                      "actions": ["Admin", "Read", "Write", "List", "Tagging"]
                    }
                  ]
                }
                """;
                return
                [
                    new ContainerFile
                    {
                        Name = "s3.json",
                        Contents = json,
                        Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                            | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                    },
                ];
            })
            .WithArgs("server", "-s3", "-s3.config=/etc/seaweedfs/s3.json", "-dir=/data");
    }

    public static IResourceBuilder<SeaweedFsResource> WithDataVolume(
        this IResourceBuilder<SeaweedFsResource> builder,
        string? name = null,
        bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(name, "/data", isReadOnly);
    }

    public static IResourceBuilder<ContainerResource> AddBucket(
        this IResourceBuilder<SeaweedFsResource> seaweed,
        string bucketName)
    {
        ArgumentNullException.ThrowIfNull(seaweed);
        ArgumentNullException.ThrowIfNull(bucketName);

        var initName = $"{seaweed.Resource.Name}-init-{bucketName}";

        var mcHostValue = ReferenceExpression.Create(
            $"http://{seaweed.Resource.AccessKeyParameter}:{seaweed.Resource.SecretKeyParameter}@host.docker.internal:{seaweed.Resource.S3Endpoint.Property(EndpointProperty.Port)}");

        return seaweed.ApplicationBuilder
            .AddContainer(initName, "minio/mc", "latest")
            .WithEnvironment("MC_HOST_seaweedfs", mcHostValue)
            .WithEntrypoint("/bin/sh")
            .WithArgs(
                "-c",
                $"until mc mb --ignore-existing seaweedfs/{bucketName}; do echo 'waiting for seaweedfs...'; sleep 1; done")
            .WaitFor(seaweed);
    }
}
