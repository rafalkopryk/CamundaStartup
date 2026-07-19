namespace ClaimsAuthorization;

public sealed record PermissionDefinition(string Key, string Name, string ResourceType);

public sealed record RoleDefinition(string Name, IReadOnlyCollection<string> Permissions);
