using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace CamundaStartup.Aspire.Hosting.Camunda;

public static class CamundaBuilderExtensions
{
    private const int DefaultGrpcPort = 26500;
    private const int DefaultRestPort = 8080;

    public static IResourceBuilder<CamundaResource> AddCamunda(this IDistributedApplicationBuilder builder, [ResourceName] string name, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);

        var zeebeContainer = new CamundaResource(name);
        return builder
            .AddResource(zeebeContainer)
            .WithHttpEndpoint(port: port, targetPort: DefaultRestPort, name: CamundaResource.RestEndpointName)
            .WithHttpEndpoint(port: DefaultGrpcPort, targetPort: DefaultGrpcPort, CamundaResource.GprcEndpointName)
            .WithHttpEndpoint(port: 9600, targetPort: 9600, name: "internal")
            .WithImage(CamundaContainerImageTags.Image, CamundaContainerImageTags.Tag)
            .WithEnvironment("CAMUNDA_SECURITY_AUTHORIZATIONS_ENABLED", "true")
            .WithEnvironment("CAMUNDA_DATA_SECONDARYSTORAGE_TYPE", "none")
            .WithEnvironment("ZEEBE_LOG_APPENDER", "Stackdriver")
            .WithEnvironment("OPERATE_LOG_APPENDER", "Stackdriver")
            .WithEnvironment("TASKLIST_LOG_APPENDER", "Stackdriver")
            .WithEnvironment("IDENTITY_LOG_APPENDER", "Stackdriver")
            .WithHttpHealthCheck("actuator/health/readiness", 200, "internal");
    }

    public static IResourceBuilder<CamundaResource> WithBasicAuth(
        this IResourceBuilder<CamundaResource> builder,
        string username = "demo",
        string password = "demo",
        string displayName = "Demo User",
        string email = "demo@demo.com")
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_METHOD", "basic")
            .WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_UNPROTECTEDAPI", "true")
            .WithEnvironment("CAMUNDA_SECURITY_INITIALIZATION_USERS[0]_USERNAME", username)
            .WithEnvironment("CAMUNDA_SECURITY_INITIALIZATION_USERS[0]_PASSWORD", password)
            .WithEnvironment("CAMUNDA_SECURITY_INITIALIZATION_USERS[0]_NAME", displayName)
            .WithEnvironment("CAMUNDA_SECURITY_INITIALIZATION_USERS[0]_EMAIL", email)
            .WithEnvironment("CAMUNDA_SECURITY_INITIALIZATION_DEFAULTROLES_ADMIN_USERS[0]", username);
    }

    public static IResourceBuilder<CamundaResource> WithElasticDatabase(this IResourceBuilder<CamundaResource> builder, ReferenceExpression? elasticConnectionString)
    {
        ArgumentNullException.ThrowIfNull(elasticConnectionString);

        builder.Resource.CamundaDatabaseConnectionStringExpression = elasticConnectionString;

        builder.WithEnvironment("CAMUNDA_DATA_SECONDARYSTORAGE_TYPE", "elasticsearch");
        builder.WithEnvironment("CAMUNDA_DATA_SECONDARYSTORAGE_ELASTICSEARCH_CLUSTERNAME", "elasticsearch");
        builder.WithEnvironment("CAMUNDA_DATA_SECONDARYSTORAGE_ELASTICSEARCH_URL", builder.Resource.CamundaDatabaseConnectionStringExpression);
        builder.WithEnvironment("CAMUNDA_DATA_SECONDARYSTORAGE_ELASTICSEARCH_NUMBEROFREPLICAS", "0");
        
        return builder;
    }
    
    public static IResourceBuilder<CamundaResource> WithRdmbsDatabase(this IResourceBuilder<CamundaResource> builder, ReferenceExpression? jdbcConnectionString, ReferenceExpression user, ParameterResource password)
    {
        builder.WithEnvironment("CAMUNDA_DATABASE_INDEX_NUMBEROFREPLICAS", "0");
        builder.WithEnvironment("CAMUNDA_DATA_SECONDARYSTORAGE_TYPE", "rdbms");
        builder.WithEnvironment("CAMUNDA_DATA_SECONDARYSTORAGE_RDBMS_URL", jdbcConnectionString);
        builder.WithEnvironment("CAMUNDA_DATA_SECONDARYSTORAGE_RDBMS_USERNAME", user);
        builder.WithEnvironment("CAMUNDA_DATA_SECONDARYSTORAGE_RDBMS_PASSWORD", password);
        
        return builder;
    }
    
    public static IResourceBuilder<CamundaResource> WithDataVolume(this IResourceBuilder<CamundaResource> builder, string? name = null, bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithVolume(name, "/usr/local/camunda/data", isReadOnly);
    }
    
    public static IResourceBuilder<CamundaResource> WithS3Backup(
        this IResourceBuilder<CamundaResource> builder,
        ReferenceExpression endpoint,
        ParameterResource accessKey,
        ParameterResource secretKey,
        string bucketName = "camunda-backup",
        string region = "us-east-1",
        bool forcePathStyleAccess = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(accessKey);
        ArgumentNullException.ThrowIfNull(secretKey);

        builder.WithEnvironment("ZEEBE_BROKER_DATA_BACKUP_STORE", "S3");
        builder.WithEnvironment("ZEEBE_BROKER_DATA_BACKUP_S3_BUCKETNAME", bucketName);
        builder.WithEnvironment("ZEEBE_BROKER_DATA_BACKUP_S3_ENDPOINT", endpoint);
        builder.WithEnvironment("ZEEBE_BROKER_DATA_BACKUP_S3_REGION", region);
        builder.WithEnvironment("ZEEBE_BROKER_DATA_BACKUP_S3_ACCESSKEY", accessKey);
        builder.WithEnvironment("ZEEBE_BROKER_DATA_BACKUP_S3_SECRETKEY", secretKey);

        if (forcePathStyleAccess)
        {
            builder.WithEnvironment("ZEEBE_BROKER_DATA_BACKUP_S3_FORCEPATHSTYLEACCESS", "true");
        }

        return builder;
    }

    public static IResourceBuilder<CamundaResource> WithOidc(
        this IResourceBuilder<CamundaResource> builder,
        string clientId,
        string clientSecret,
        ReferenceExpression redirectUri,
        ReferenceExpression? issuerUri = null,
        ReferenceExpression? authorizationUri = null,
        ReferenceExpression? tokenUri = null,
        ReferenceExpression? jwkSetUri = null,
        ReferenceExpression? endSessionEndpointUri = null,
        string usernameClaim = "preferred_username",
        string clientIdClaim = "azp",
        string? groupsClaim = null,
        string[]? audiences = null,
        string[]? scope = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(clientSecret);
        ArgumentNullException.ThrowIfNull(redirectUri);

        var hasExplicitEndpoints = authorizationUri is not null || tokenUri is not null
            || jwkSetUri is not null || endSessionEndpointUri is not null;

        if (issuerUri is null && !hasExplicitEndpoints)
        {
            throw new ArgumentException(
                "Either issuerUri or the explicit endpoint URIs (authorizationUri, tokenUri, jwkSetUri, endSessionEndpointUri) must be provided.");
        }

        if (issuerUri is not null && hasExplicitEndpoints)
        {
            throw new ArgumentException(
                "issuerUri and the explicit endpoint URIs are mutually exclusive. Use issuerUri for discovery, or pass the four explicit endpoints when browser and backend need different URLs.");
        }

        audiences ??= [clientId, "orchestration-api"];
        scope ??= ["openid", "profile", "email"];

        builder
            .WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_METHOD", "oidc")
            .WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_OIDC_CLIENTID", clientId)
            .WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_OIDC_CLIENTSECRET", clientSecret)
            .WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_OIDC_REDIRECTURI", redirectUri)
            .WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_OIDC_USERNAMECLAIM", usernameClaim)
            .WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_OIDC_CLIENTIDCLAIM", clientIdClaim);

        if (issuerUri is not null)
        {
            builder.WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_OIDC_ISSUERURI", issuerUri);
        }

        if (authorizationUri is not null)
        {
            builder.WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_OIDC_AUTHORIZATIONURI", authorizationUri);
        }

        if (tokenUri is not null)
        {
            builder.WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_OIDC_TOKENURI", tokenUri);
        }

        if (jwkSetUri is not null)
        {
            builder.WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_OIDC_JWKSETURI", jwkSetUri);
        }

        if (endSessionEndpointUri is not null)
        {
            builder.WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_OIDC_ENDSESSIONENDPOINTURI", endSessionEndpointUri);
        }

        for (var i = 0; i < audiences.Length; i++)
        {
            builder.WithEnvironment($"CAMUNDA_SECURITY_AUTHENTICATION_OIDC_AUDIENCES_{i}", audiences[i]);
        }

        for (var i = 0; i < scope.Length; i++)
        {
            builder.WithEnvironment($"CAMUNDA_SECURITY_AUTHENTICATION_OIDC_SCOPE_{i}", scope[i]);
        }

        
        builder.WithEnvironment("CAMUNDA_SECURITY_AUTHENTICATION_OIDC_GROUPSCLAIM", groupsClaim);

        builder.WithEnvironment("CAMUNDA_SECURITY_INITIALIZATION_DEFAULTROLES_ADMIN_GROUPS_0", "admin");

        
        
        return builder;
    }
}
