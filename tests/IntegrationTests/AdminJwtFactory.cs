using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EwaysVNext.Core.Application.Security;
using Microsoft.IdentityModel.Tokens;

namespace EwaysVNext.IntegrationTests;

internal static class AdminJwtFactory
{
    public const string Issuer = "https://integration-tests.local/realms/test";
    public const string Audience = "modular-monolith";
    public const string SigningKey = "EwaysVNext-Integration-Tests-Signing-Key-Do-Not-Use-In-Prod-2026!!";

    public static string CreateToken(
        string tenant,
        string role,
        Guid? sub = null,
        IEnumerable<Claim>? extra = null,
        TimeSpan? lifetime = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, (sub ?? Guid.NewGuid()).ToString()),
            new(ClaimTypes.Name, $"integration-test-{role}"),
            new(ClaimTypes.Email, $"{role}@integration-tests.local"),
            // ClaimTypes.Role (the long URI), not the short "role" name — that way the
            // claim survives verbatim regardless of whichever inbound-claim-mapping default
            // the JWT handler is currently using.
            new(ClaimTypes.Role, role),
            new(EwaysClaimTypes.Tenant, tenant),
        };

        if (extra is not null)
            claims.AddRange(extra);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(30)),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string CreateSuperAdminToken(Guid? sub = null) =>
        CreateToken(AdminRoles.BackofficeRealm, AdminRoles.SuperAdmin, sub);

    public static string CreateStaffToken(Guid? sub = null) =>
        CreateToken(AdminRoles.BackofficeRealm, AdminRoles.Staff, sub);

    public static string CreateShopToken(Guid sub, string email, bool emailVerified) =>
        CreateToken(AdminRoles.ShopRealm, "customer", sub,
        [
            new Claim(EwaysClaimTypes.Email, email),
            new Claim(EwaysClaimTypes.EmailVerified, emailVerified ? "true" : "false"),
        ]);

    /// <summary>
    /// The broadest admin a test can hold. It mints <c>super_admin</c> now that the two
    /// backoffice roles replaced the old single "admin" role, so existing tests keep passing.
    /// </summary>
    public static string CreateAdminToken() => CreateSuperAdminToken();

    public static string CreateCustomerToken() =>
        CreateToken(AdminRoles.ShopRealm, "customer");
}
