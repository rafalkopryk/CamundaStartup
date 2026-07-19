using System.Security.Claims;
using System.Text.Json;

namespace ClaimsAuthorization;

internal static class TokenClaimValues
{
    public static IEnumerable<string> Get(ClaimsPrincipal principal, string claimType)
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
                    // Treat malformed JSON as a scalar claim.
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
}

public static class TokenAccessClaimsPrincipalExtensions
{
    public static bool HasCif(this ClaimsPrincipal principal, string cif) =>
        HasClaimValue(principal, "Cif", cif);

    public static bool HasXnuc(this ClaimsPrincipal principal, string xnuc) =>
        HasClaimValue(principal, "Xnuc", xnuc);

    private static bool HasClaimValue(
        ClaimsPrincipal principal,
        string claimType,
        string expectedValue) =>
        !string.IsNullOrWhiteSpace(expectedValue) &&
        TokenClaimValues.Get(principal, claimType)
            .Any(value => value.Equals(expectedValue, StringComparison.OrdinalIgnoreCase));
}
