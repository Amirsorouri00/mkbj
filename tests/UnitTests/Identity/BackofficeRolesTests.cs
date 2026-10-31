using System.Security.Claims;
using AwesomeAssertions;
using EwaysVNext.Core.Application.Security;
using Xunit;

namespace EwaysVNext.UnitTests.Identity;

/// <summary>
/// The one decision both enforcement paths rely on: the REST permission handler and the GraphQL
/// field attribute. They have drifted twice — first the GraphQL path forwarded no roles at all,
/// which made every admin GraphQL field refuse every caller once backoffice authority moved to
/// Keycloak. Covering the shared helper is what stops the third time.
/// </summary>
public sealed class BackofficeRolesTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void Backoffice_Roles_Are_Forwarded() =>
        BackofficeRoles.From(Principal(
                (EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm),
                (ClaimTypes.Role, AdminRoles.Staff)))
            .Should().BeEquivalentTo([AdminRoles.Staff]);

    [Fact]
    public void Shop_Roles_Are_Not() =>
        BackofficeRoles.From(Principal(
                (EwaysClaimTypes.Tenant, AdminRoles.ShopRealm),
                (ClaimTypes.Role, AdminRoles.SuperAdmin)))
            .Should().BeNull("self-service registration must not be a route to administration");

    [Fact]
    public void A_Token_With_No_Tenant_Is_Not() =>
        BackofficeRoles.From(Principal((ClaimTypes.Role, AdminRoles.SuperAdmin)))
            .Should().BeNull();

    [Fact]
    public void A_Backoffice_Token_With_No_Roles_Yields_Null() =>
        BackofficeRoles.From(Principal((EwaysClaimTypes.Tenant, AdminRoles.BackofficeRealm)))
            .Should().BeNull("null and empty mean the same thing to the check service; say it once");

    [Fact]
    public void A_Null_Principal_Is_Handled() =>
        BackofficeRoles.From(null).Should().BeNull();
}
