using System.Text.Json;

namespace ClaimsAuthorization;

public interface IPermissionCatalog
{
    IReadOnlyCollection<string> PermissionKeys { get; }

    bool RoleHasPermission(IEnumerable<string> roles, string permissionKey);

    IReadOnlySet<string> GetPermissions(IEnumerable<string> roles);

}

public sealed class PermissionCatalog : IPermissionCatalog
{
    private readonly IReadOnlyDictionary<string, PermissionDefinition> _permissions;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _permissionsByRole;

    private PermissionCatalog(
        IReadOnlyDictionary<string, PermissionDefinition> permissions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> permissionsByRole)
    {
        _permissions = permissions;
        _permissionsByRole = permissionsByRole;
    }

    public IReadOnlyCollection<string> PermissionKeys => _permissions.Keys.ToArray();

    public bool RoleHasPermission(IEnumerable<string> roles, string permissionKey)
    {
        if (!_permissions.ContainsKey(permissionKey))
        {
            return false;
        }

        return roles.Any(role =>
            _permissionsByRole.TryGetValue(role, out var permissions) &&
            permissions.Contains(permissionKey));
    }

    public IReadOnlySet<string> GetPermissions(IEnumerable<string> roles)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            if (_permissionsByRole.TryGetValue(role, out var permissions))
            {
                result.UnionWith(permissions);
            }
        }

        return result;
    }

    public static PermissionCatalog Load(string permissionsPath, string rolesPath)
    {
        var serializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var permissionsFile = Deserialize<PermissionsFile>(permissionsPath, serializerOptions);
        var rolesFile = Deserialize<RolesFile>(rolesPath, serializerOptions);
        var permissions = BuildPermissionIndex(permissionsFile.Permissions);
        return new PermissionCatalog(permissions, BuildRoleIndex(rolesFile.Roles, permissions));
    }

    private static T Deserialize<T>(string path, JsonSerializerOptions options)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, options)
                   ?? throw new InvalidOperationException($"Authorization file '{path}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Authorization file '{path}' contains invalid JSON.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Authorization file '{path}' could not be read.", exception);
        }
    }

    private static IReadOnlyDictionary<string, PermissionDefinition> BuildPermissionIndex(
        IReadOnlyCollection<PermissionDefinition>? definitions)
    {
        if (definitions is null)
        {
            throw new InvalidOperationException("The permissions file must contain a 'permissions' array.");
        }

        var result = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);
        foreach (var permission in definitions)
        {
            ValidateNotBlank(permission.Key, "permission key");
            ValidateNotBlank(permission.Name, $"name of permission '{permission.Key}'");
            ValidateNotBlank(permission.ResourceType, $"resource type of permission '{permission.Key}'");
            if (!result.TryAdd(permission.Key, permission))
            {
                throw new InvalidOperationException($"Permission key '{permission.Key}' is defined more than once.");
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> BuildRoleIndex(
        IReadOnlyCollection<RoleDefinition>? definitions,
        IReadOnlyDictionary<string, PermissionDefinition> permissions)
    {
        if (definitions is null)
        {
            throw new InvalidOperationException("The roles file must contain a 'roles' array.");
        }

        var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var role in definitions)
        {
            ValidateNotBlank(role.Name, "role name");
            if (role.Permissions is null)
            {
                throw new InvalidOperationException($"Role '{role.Name}' must contain a 'permissions' array.");
            }

            var rolePermissions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var permissionKey in role.Permissions)
            {
                ValidateNotBlank(permissionKey, $"permission key in role '{role.Name}'");
                if (!permissions.ContainsKey(permissionKey))
                {
                    throw new InvalidOperationException(
                        $"Role '{role.Name}' references undefined permission '{permissionKey}'.");
                }

                if (!rolePermissions.Add(permissionKey))
                {
                    throw new InvalidOperationException(
                        $"Role '{role.Name}' references permission '{permissionKey}' more than once.");
                }
            }

            if (!result.TryAdd(role.Name, rolePermissions))
            {
                throw new InvalidOperationException($"Role '{role.Name}' is defined more than once.");
            }
        }

        return result;
    }

    private static void ValidateNotBlank(string? value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The {description} must not be blank.");
        }
    }

    private sealed record PermissionsFile(IReadOnlyCollection<PermissionDefinition>? Permissions);
    private sealed record RolesFile(IReadOnlyCollection<RoleDefinition>? Roles);
}
