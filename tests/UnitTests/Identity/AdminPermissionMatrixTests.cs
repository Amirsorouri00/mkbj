using System.Security.Claims;
using AuthorizationService.Abstractions.Policies;
using AuthorizationService.Abstractions.Requirements;
using AuthorizationService.Implementation.Handlers;
using AuthorizationService.Implementation.Persistence;
using AuthorizationService.Implementation.Seeding;
using AuthorizationService.Implementation.Services;
using AwesomeAssertions;
using EwaysVNext.Core.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EwaysVNext.UnitTests.Identity;

/// <summary>
/// The spec §6.1 matrix as an executable table. Staff run the shop day to day; a super admin
/// alone may touch partners, money and other admins. The rows are seeded data, so this asserts
/// the seed rather than a hard-coded branch — the two drift otherwise.
/// </summary>
public sealed class AdminPermissionMatrixTests : IDisposable
{
    private readonly AuthorizationDbContext _db;

    public AdminPermissionMatrixTests()
    {
        var options = new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseInMemoryDatabase($"matrix-{Guid.NewGuid()}")
            .Options;

        _db = new AuthorizationDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    private async Task<bool> Evaluate(string tenant, string role, string permission)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(EwaysClaimTypes.Tenant, tenant),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role),
            ],
            "test"));

        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);

        await new PermissionAuthorizationHandler(new AuthorizationCheckService(_db))
            .HandleAsync(context);

        return context.HasSucceeded;
    }

    [Theory]
    [InlineData(AdminRoles.Staff, AdminPermissions.CatalogManage, true)]
    [InlineData(AdminRoles.Staff, AdminPermissions.ImportsManage, true)]
    [InlineData(AdminRoles.Staff, AdminPermissions.CustomersManage, true)]
    [InlineData(AdminRoles.Staff, AdminPermissions.OrdersManage, true)]
    [InlineData(AdminRoles.Staff, AdminPermissions.PaymentsConfirm, true)]
    [InlineData(AdminRoles.Staff, AdminPermissions.PartnersManage, false)]
    [InlineData(AdminRoles.Staff, AdminPermissions.FxManage, false)]
    [InlineData(AdminRoles.Staff, AdminPermissions.CurrenciesManage, false)]
    [InlineData(AdminRoles.Staff, AdminPermissions.SettingsManage, false)]
    [InlineData(AdminRoles.Staff, AdminPermissions.RefundsManage, false)]
    [InlineData(AdminRoles.Staff, AdminPermissions.AdminAccountsManage, false)]
    [InlineData(AdminRoles.SuperAdmin, AdminPermissions.PartnersManage, true)]
    [InlineData(AdminRoles.SuperAdmin, AdminPermissions.RefundsManage, true)]
    [InlineData(AdminRoles.SuperAdmin, AdminPermissions.CatalogManage, true)]
    public async Task Backoffice_Role_Claim_Maps_To_Seeded_Matrix(string role, string permission, bool allowed) =>
        (await Evaluate(AdminRoles.BackofficeRealm, role, permission)).Should().Be(allowed);

    [Fact]
    public async Task Roles_From_A_Shop_Token_Are_Ignored() =>
        (await Evaluate(AdminRoles.ShopRealm, AdminRoles.SuperAdmin, AdminPermissions.CatalogManage))
            .Should().BeFalse("a customer realm must never mint backoffice authority");

    [Fact]
    public async Task A_Token_With_No_Tenant_Is_Ignored() =>
        (await Evaluate(tenant: "", AdminRoles.SuperAdmin, AdminPermissions.CatalogManage))
            .Should().BeFalse();

    [Fact]
    public void Matrix_Covers_Every_AdminPermissions_Constant() =>
        AdminPermissionMatrix.Rows.Select(r => r.Code).Should().BeEquivalentTo(
            typeof(AdminPermissions).GetFields()
                .Where(f => f.IsLiteral)
                .Select(f => (string)f.GetRawConstantValue()!));

    [Fact]
    public void Every_Matrix_Identifier_Is_Distinct() =>
        AdminPermissionMatrix.Rows
            .SelectMany(r => new[] { r.PermissionId, r.RuleId })
            .Should().OnlyHaveUniqueItems("a reused seed GUID collapses two rows into one");
}
