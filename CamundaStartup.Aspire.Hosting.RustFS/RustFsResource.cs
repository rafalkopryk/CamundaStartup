using Aspire.Hosting.ApplicationModel;

namespace CamundaStartup.Aspire.Hosting.RustFS;

public sealed class RustFsResource(string name, ParameterResource accessKey, ParameterResource secretKey)
    : ContainerResource(name), IResourceWithConnectionString
{
    internal const string S3EndpointName = "s3";
    internal const string ConsoleEndpointName = "console";

    public ParameterResource AccessKeyParameter { get; } = accessKey;

    public ParameterResource SecretKeyParameter { get; } = secretKey;

    private EndpointReference? _s3Reference;
    public EndpointReference S3Endpoint => _s3Reference ??= new(this, S3EndpointName);

    private EndpointReference? _consoleReference;
    public EndpointReference ConsoleEndpoint => _consoleReference ??= new(this, ConsoleEndpointName);

    public ReferenceExpression S3EndpointExpression =>
        ReferenceExpression.Create(
            $"{S3Endpoint.Property(EndpointProperty.Scheme)}://{S3Endpoint.Property(EndpointProperty.Host)}:{S3Endpoint.Property(EndpointProperty.Port)}"
        );

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"Endpoint={S3Endpoint.Property(EndpointProperty.Scheme)}://{S3Endpoint.Property(EndpointProperty.Host)}:{S3Endpoint.Property(EndpointProperty.Port)};AccessKey={AccessKeyParameter};SecretKey={SecretKeyParameter}"
        );
}
