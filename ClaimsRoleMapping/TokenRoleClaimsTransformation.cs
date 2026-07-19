using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ClaimsAuthorization;

public interface ITokenRoleResolver
{
    IReadOnlySet<string> Resolve(ClaimsPrincipal principal);
}

public sealed class TokenRoleResolver(IOptions<TokenRoleMappingOptions> options) : ITokenRoleResolver
{
    private const string OutputClaimType = "roles";
    private const string ClientIdClaimType = "client_id";
    private const string CifClaimType = "Cif";
    private const string XnucClaimType = "Xnuc";
    private static readonly string[] ScopeClaimTypes = ["scope", "Scope"];
    private static readonly string[] SourceRoleClaimTypes = ["Role", "Roles"];

    public IReadOnlySet<string> Resolve(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var roles = new HashSet<string>(
            principal.Claims
                .Where(claim => claim.Type.Equals(OutputClaimType, StringComparison.Ordinal))
                .Select(claim => claim.Value),
            StringComparer.Ordinal);

        var clientIds = GetValues(principal, ClientIdClaimType).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasConcreteCif = GetValues(principal, CifClaimType).Any(IsConcreteValue);
        var hasConcreteXnuc = GetValues(principal, XnucClaimType).Any(IsConcreteValue);
        var scopes = GetValues(principal, ScopeClaimTypes)
            .SelectMany(SplitValues)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceRoles = GetValues(principal, SourceRoleClaimTypes)
            .SelectMany(SplitValues)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matchingMappings = options.Value.Mappings.Where(mapping =>
            Matches(mapping.ClientIds, clientIds) &&
            Matches(mapping.Scopes, scopes) &&
            Matches(mapping.Roles, sourceRoles) &&
            (!mapping.RequireCif || hasConcreteCif) &&
            (!mapping.RequireXnuc || hasConcreteXnuc));

        foreach (var mapping in SelectMappings(matchingMappings))
        {
            roles.Add(mapping.Role);
        }

        return roles;
    }

    private static bool IsConcreteValue(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Equals("*", StringComparison.OrdinalIgnoreCase) &&
        !value.Equals("null", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitValues(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Matches(IReadOnlyCollection<string>? expected, IReadOnlySet<string> actual) =>
        expected is null || expected.Count == 0 || expected.Any(actual.Contains);

    private static IEnumerable<TokenRoleMapping> SelectMappings(IEnumerable<TokenRoleMapping> mappings)
    {
        var matches = mappings.ToArray();

        foreach (var mapping in matches.Where(mapping => string.IsNullOrWhiteSpace(mapping.ExclusiveGroup)))
        {
            yield return mapping;
        }

        foreach (var group in matches
                     .Where(mapping => !string.IsNullOrWhiteSpace(mapping.ExclusiveGroup))
                     .GroupBy(mapping => mapping.ExclusiveGroup!, StringComparer.OrdinalIgnoreCase))
        {
            yield return group.OrderByDescending(mapping => mapping.Priority).First();
        }
    }

    private static IEnumerable<string> GetValues(ClaimsPrincipal principal, string claimType)
    {
        foreach (var claim in principal.Claims.Where(claim =>
                     claim.Type.Equals(claimType, StringComparison.Ordinal)))
        {
            if (claim.Value.StartsWith("[", StringComparison.Ordinal))
            {
                string[]? values = null;
                try
                {
                    values = JsonSerializer.Deserialize<string[]>(claim.Value);
                }
                catch (JsonException)
                {
                    // Treat a malformed/non-JSON claim as a normal scalar value.
                }

                if (values is not null)
                {
                    foreach (var value in values)
                    {
                        yield return value;
                    }

                    continue;
                }
            }

            yield return claim.Value;
        }
    }

    private static IEnumerable<string> GetValues(
        ClaimsPrincipal principal,
        IEnumerable<string> claimTypes) =>
        claimTypes.SelectMany(claimType => GetValues(principal, claimType));
}
