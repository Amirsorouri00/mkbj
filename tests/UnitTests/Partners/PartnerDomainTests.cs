using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Partners.Application.Partners.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Partners;

/// <summary>
/// The partner aggregate, which owns its keys. Time is a parameter throughout, so every one of
/// these is deterministic.
/// </summary>
public sealed class PartnerDomainTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;

    private static byte[] Hash() => [1, 2, 3, 4];

    private static Partner Acme(params string[] scopes) =>
        Partner.Create(1, "Acme", scopes.Length == 0 ? [PartnerScopes.OrdersCreate] : scopes, 300, 100, Now);

    private static Partner PartnerWithKey(long keyId, string[] scopes)
    {
        var partner = Acme(scopes);
        partner.IssueKey(keyId, "OLDPREF1", Hash(), scopes, expiresAt: null, Now);
        return partner;
    }

    private static string CodeOf(Action act) =>
        FluentActions.Invoking(act).Should().Throw<DomainException>().Which.Code;

    [Fact]
    public void Create_Rejects_An_Unknown_Scope() =>
        CodeOf(() => Partner.Create(1, "Acme", ["orders:delete"], 300, 100, Now))
            .Should().Be("VALIDATION_FAILED");

    [Theory]
    [InlineData(0, 100)]
    [InlineData(6001, 100)]
    [InlineData(300, 0)]
    [InlineData(300, 1001)]
    public void Create_Enforces_Limit_Bounds(int rate, int bulk) =>
        CodeOf(() => Partner.Create(1, "Acme", [PartnerScopes.OrdersCreate], rate, bulk, Now))
            .Should().Be("VALIDATION_FAILED");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_A_Blank_Name(string name) =>
        CodeOf(() => Partner.Create(1, name, [PartnerScopes.OrdersCreate], 300, 100, Now))
            .Should().Be("VALIDATION_FAILED");

    [Fact]
    public void Create_Rejects_A_Name_Past_The_Limit() =>
        CodeOf(() => Partner.Create(1, new string('x', PartnerLimits.NameMaxLength + 1),
                [PartnerScopes.OrdersCreate], 300, 100, Now))
            .Should().Be("VALIDATION_FAILED");

    [Fact]
    public void Create_Trims_The_Name() =>
        Partner.Create(1, "  Acme  ", [PartnerScopes.OrdersCreate], 300, 100, Now)
            .Name.Should().Be("Acme");

    [Fact]
    public void IssueKey_Defaults_To_All_Partner_Scopes()
    {
        var partner = Acme(PartnerScopes.OrdersCreate, PartnerScopes.OrdersRead);

        partner.IssueKey(10, "PREFIX01", Hash(), scopes: null, expiresAt: null, Now)
            .Scopes.Should().BeEquivalentTo([PartnerScopes.OrdersCreate, PartnerScopes.OrdersRead]);
    }

    [Fact]
    public void IssueKey_Rejects_Scopes_Outside_The_Partner() =>
        CodeOf(() => Acme(PartnerScopes.OrdersRead)
                .IssueKey(10, "PREFIX01", Hash(), [PartnerScopes.OrdersCreate], null, Now))
            .Should().Be("VALIDATION_FAILED");

    [Fact]
    public void IssueKey_Rejects_Expiry_In_The_Past() =>
        CodeOf(() => Acme().IssueKey(10, "PREFIX01", Hash(), null, expiresAt: Now, Now))
            .Should().Be("VALIDATION_FAILED");

    [Fact]
    public void Rotate_Gives_Old_Key_Overlap_Expiry_And_New_Key_Same_Scopes()
    {
        var partner = PartnerWithKey(10, [PartnerScopes.OrdersCreate]);

        var fresh = partner.RotateKey(10, 11, "NEWPREF1", Hash(), TimeSpan.FromHours(24), Now);

        partner.Keys.Single(k => k.Id == 10).ExpiresAt.Should().Be(Now.AddHours(24));
        fresh.Scopes.Should().BeEquivalentTo([PartnerScopes.OrdersCreate]);
        fresh.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void Rotate_Never_Extends_An_Earlier_Expiry()
    {
        var partner = Acme();
        partner.IssueKey(10, "OLDPREF1", Hash(), null, expiresAt: Now.AddMinutes(10), Now);

        partner.RotateKey(10, 11, "NEWPREF1", Hash(), TimeSpan.FromHours(24), Now);

        partner.Keys.Single(k => k.Id == 10).ExpiresAt.Should().Be(Now.AddMinutes(10),
            "rotating a key must never give it longer to live than it already had");
    }

    [Fact]
    public void Rotate_A_Revoked_Key_Is_A_Conflict()
    {
        var partner = PartnerWithKey(10, [PartnerScopes.OrdersCreate]);
        partner.RevokeKey(10, Now);

        CodeOf(() => partner.RotateKey(10, 11, "NEWPREF1", Hash(), TimeSpan.FromHours(24), Now))
            .Should().Be("CONFLICT");
    }

    [Fact]
    public void Rotate_An_Unknown_Key_Is_Not_Found() =>
        CodeOf(() => Acme().RotateKey(99, 11, "NEWPREF1", Hash(), TimeSpan.FromHours(24), Now))
            .Should().Be("NOT_FOUND");

    [Fact]
    public void Revoke_Is_Idempotent()
    {
        var partner = PartnerWithKey(10, [PartnerScopes.OrdersCreate]);

        partner.RevokeKey(10, Now);
        partner.RevokeKey(10, Now.AddHours(1));

        partner.Keys.Single(k => k.Id == 10).RevokedAt.Should().Be(Now,
            "a second revoke must not move the moment the key stopped working");
    }

    [Fact]
    public void SetScopes_Trims_Existing_Key_Scopes()
    {
        var partner = Acme(PartnerScopes.OrdersCreate, PartnerScopes.OrdersRead);
        partner.IssueKey(10, "PREFIX01", Hash(), null, null, Now);

        partner.SetScopes([PartnerScopes.OrdersRead]);

        partner.Keys.Single().Scopes.Should().BeEquivalentTo([PartnerScopes.OrdersRead],
            "narrowing a partner must narrow every key it already issued, or the revocation "
            + "is cosmetic");
    }

    [Fact]
    public void Key_Is_Not_Usable_At_Its_Exact_Expiry()
    {
        var partner = Acme();
        var key = partner.IssueKey(10, "PREFIX01", Hash(), null, expiresAt: Now.AddHours(1), Now);

        key.IsUsableAt(Now.AddHours(1).AddTicks(-1)).Should().BeTrue();
        key.IsUsableAt(Now.AddHours(1)).Should().BeFalse();
    }

    [Fact]
    public void A_Revoked_Key_Is_Never_Usable()
    {
        var partner = PartnerWithKey(10, [PartnerScopes.OrdersCreate]);
        partner.RevokeKey(10, Now);

        partner.Keys.Single().IsUsableAt(Now.AddYears(-1)).Should().BeFalse();
    }

    [Fact]
    public void Suspend_And_Reactivate_Move_The_Status()
    {
        var partner = Acme();
        partner.Status.Should().Be(PartnerStatus.Active);

        partner.Suspend();
        partner.Status.Should().Be(PartnerStatus.Suspended);

        partner.Reactivate();
        partner.Status.Should().Be(PartnerStatus.Active);
    }

    [Fact]
    public void SetLimits_Enforces_The_Same_Bounds_As_Create() =>
        CodeOf(() => Acme().SetLimits(PartnerLimits.RateLimitMax + 1, 100))
            .Should().Be("VALIDATION_FAILED");
}
