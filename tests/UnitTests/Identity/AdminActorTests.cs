using System.Security.Claims;
using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Core.Application.Security;
using Xunit;

namespace EwaysVNext.UnitTests.Identity;

/// <summary>
/// An admin actor is read from the token and nowhere else. The tenant check is the part that
/// matters: a Shop realm is where customers sign themselves up, so a role claim minted there must
/// never reach the backoffice permission matrix (spec §6.1).
/// </summary>
public sealed class AdminActorTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Theory]
    [InlineData(AdminRoles.BackofficeRealm, AdminRoles.SuperAdmin, true, true)]
    [InlineData(AdminRoles.BackofficeRealm, AdminRoles.Staff, true, false)]
    [InlineData(AdminRoles.ShopRealm, AdminRoles.SuperAdmin, false, false)]
    [InlineData(AdminRoles.BackofficeRealm, "customer", false, false)]
    public void TryFrom_Reads_Tenant_Sub_And_Role(string tenant, string role, bool ok, bool isSuper)
    {
        var sub = Guid.NewGuid();
        var user = Principal(
            (EwaysClaimTypes.Tenant, tenant),
            (EwaysClaimTypes.Subject, sub.ToString()),
            (ClaimTypes.Role, role));

        AdminActor.TryFrom(user, out var actor).Should().Be(ok);

        if (ok)
            actor.Should().Be(new AdminActor(sub, isSuper));
    }

    [Fact]
    public void TryFrom_Rejects_A_Non_Guid_Sub() =>
        AdminActor.TryFrom(
            Principal(
                (EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm),
                (EwaysClaimTypes.Subject, "abc"),
                (ClaimTypes.Role, AdminRoles.Staff)),
            out _).Should().BeFalse();

    [Fact]
    public void TryFrom_Rejects_A_Token_With_No_Tenant() =>
        AdminActor.TryFrom(
            Principal(
                (EwaysClaimTypes.Subject, Guid.NewGuid().ToString()),
                (ClaimTypes.Role, AdminRoles.SuperAdmin)),
            out _).Should().BeFalse();

    [Fact]
    public void A_Super_Admin_Carrying_Both_Roles_Is_Still_Super()
    {
        var sub = Guid.NewGuid();
        var user = Principal(
            (EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm),
            (EwaysClaimTypes.Subject, sub.ToString()),
            (ClaimTypes.Role, AdminRoles.Staff),
            (ClaimTypes.Role, AdminRoles.SuperAdmin));

        AdminActor.TryFrom(user, out var actor).Should().BeTrue();
        actor.Should().Be(new AdminActor(sub, IsSuperAdmin: true));
    }

    [Fact]
    public void NameIdentifier_Stands_In_For_A_Missing_Sub()
    {
        // ASP.NET Core maps "sub" to NameIdentifier under the default inbound claim map, so a
        // principal built by the JWT handler may carry only the mapped name.
        var sub = Guid.NewGuid();
        var user = Principal(
            (EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm),
            (ClaimTypes.NameIdentifier, sub.ToString()),
            (ClaimTypes.Role, AdminRoles.Staff));

        AdminActor.TryFrom(user, out var actor).Should().BeTrue();
        actor!.UserId.Should().Be(sub);
    }
}
