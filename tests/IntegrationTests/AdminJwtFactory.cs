using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EwaysVNext.Host.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace EwaysVNext.IntegrationTests;

internal static class AdminJwtFactory
{
    public const string Issuer = "https://integration-tests.local/realms/test";
    public const string Audience = "modular-monolith";
    public const string SigningKey = "EwaysVNext-Integration-Tests-Signing-Key-Do-Not-Use-In-Prod-2026!!";

    private static string CreateToken(string tenant, string role, TimeSpan? lifetime = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, $"integration-test-{role}"),
            new Claim(ClaimTypes.Email, $"{role}@integration-tests.local"),
            // ClaimTypes.Role (the long URI), not the short "role" name — that way the
            // claim survives verbatim regardless of whichever inbound-claim-mapping default
            // the JWT handler is currently using.
            new Claim(ClaimTypes.Role, role),
            new Claim(KeycloakClaimTypes.Tenant, tenant),
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(30)),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string CreateAdminToken() => CreateToken("Backoffice", "admin");
    public static string CreateCustomerToken() => CreateToken("Shop", "customer");
}
