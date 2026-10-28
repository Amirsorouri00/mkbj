using System.Security.Claims;
using AwesomeAssertions;
using EwaysVNext.Core.Application;
using Xunit;

namespace EwaysVNext.UnitTests;

/// <summary>
/// A partner's identity comes from its authenticated key and nowhere else (spec §6.2) — there is
/// no request property to bind it from, so a caller cannot name a partner it is not.
/// </summary>
public sealed class PrincipalTests
{
    [Fact]
    public void Partner_Principal_From_Claims()
    {
        var user = Principal(("partner_id", "42"), ("api_key_id", "7"),
            ("scope", "orders:create"), ("scope", "orders:read"));

        PartnerPrincipal.TryFrom(user, out var principal).Should().BeTrue();

        principal!.PartnerId.Should().Be(42L);
        principal.ApiKeyId.Should().Be(7L);
        principal.Has(PartnerScopes.OrdersCreate).Should().BeTrue();
        principal.Has(PartnerScopes.InvoicesRead).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "7")]
    [InlineData("abc", "7")]
    [InlineData("42", null)]
    [InlineData("0", "7")]
    [InlineData("-1", "7")]
    public void Partner_Principal_Requires_Positive_Numeric_Ids(string? partnerId, string? keyId)
    {
        var claims = new List<(string, string)>();
        if (partnerId is not null) claims.Add(("partner_id", partnerId));
        if (keyId is not null) claims.Add(("api_key_id", keyId));

        PartnerPrincipal.TryFrom(Principal([.. claims]), out _).Should().BeFalse();
    }

    [Fact]
    public void Unknown_Scopes_Are_Dropped()
    {
        var user = Principal(("partner_id", "42"), ("api_key_id", "7"),
            ("scope", "orders:read"), ("scope", "admin:all"));

        PartnerPrincipal.TryFrom(user, out var principal).Should().BeTrue();

        principal!.Scopes.Should().BeEquivalentTo([PartnerScopes.OrdersRead]);
        principal.Has("admin:all").Should().BeFalse();
    }

    [Fact]
    public void Scopes_Constants() =>
        PartnerScopes.All.Should().BeEquivalentTo(
            ["customers:read", "customers:write", "orders:create", "orders:read", "invoices:read"]);

    [Theory]
    [InlineData(true, "super_admin")]
    [InlineData(false, "staff")]
    public void AdminActor_Maps_To_Audit_Role(bool super, string role) =>
        new AdminActor(Guid.Parse("11111111-1111-1111-1111-111111111111"), super)
            .ToAudit().Role.Should().Be(role);

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));
}
