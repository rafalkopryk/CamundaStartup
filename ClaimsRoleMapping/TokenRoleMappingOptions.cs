using System.Text.Json;

namespace ClaimsAuthorization;

public sealed class TokenRoleMappingOptions
{
    public IReadOnlyCollection<TokenRoleMapping> Mappings { get; init; } = [];

    internal static TokenRoleMappingOptions Load(string rolesPath)
    {
        try
        {
            using var stream = File.OpenRead(rolesPath);
            return JsonSerializer.Deserialize<TokenRoleMappingOptions>(stream, new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true,
                   })
                   ?? throw new InvalidOperationException($"Authorization file '{rolesPath}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Authorization file '{rolesPath}' contains invalid JSON.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Authorization file '{rolesPath}' could not be read.", exception);
        }
    }
}

public sealed record TokenRoleMapping(
    string Key,
    string Role,
    IReadOnlyCollection<string>? ClientIds = null,
    IReadOnlyCollection<string>? Scopes = null,
    IReadOnlyCollection<string>? Roles = null,
    bool RequireCif = false,
    bool RequireXnuc = false,
    string? ExclusiveGroup = null,
    int Priority = 0);
