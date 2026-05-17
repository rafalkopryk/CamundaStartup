using Aspire.Hosting.ApplicationModel;

namespace CamundaStartup.Aspire.Hosting.SeaweedFS;

public sealed class SeaweedFsResource(string name, ParameterResource accessKey, ParameterResource secretKey)
    : ContainerResource(name), IResourceWithConnectionString
{
    internal const string S3EndpointName = "s3";
    internal const string MasterEndpointName = "master";
    internal const string FilerEndpointName = "filer";

    public ParameterResource AccessKeyParameter { get; } = accessKey;

    public ParameterResource SecretKeyParameter { get; } = secretKey;

    private EndpointReference? _s3Reference;
    public EndpointReference S3Endpoint => _s3Reference ??= new(this, S3EndpointName);

    private EndpointReference? _masterReference;
    public EndpointReference MasterEndpoint => _masterReference ??= new(this, MasterEndpointName);

    private EndpointReference? _filerReference;
    public EndpointReference FilerEndpoint => _filerReference ??= new(this, FilerEndpointName);

    public ReferenceExpression S3EndpointExpression =>
        ReferenceExpression.Create(
            $"{S3Endpoint.Property(EndpointProperty.Scheme)}://{S3Endpoint.Property(EndpointProperty.Host)}:{S3Endpoint.Property(EndpointProperty.Port)}"
        );

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"Endpoint={S3Endpoint.Property(EndpointProperty.Scheme)}://{S3Endpoint.Property(EndpointProperty.Host)}:{S3Endpoint.Property(EndpointProperty.Port)};AccessKey={AccessKeyParameter};SecretKey={SecretKeyParameter}"
        );
}
