using AuthorizationService.Abstractions.Policies;

namespace AuthorizationService.Implementation.Seeding;

/// <summary>
/// The spec §6.1 matrix as seed data: one permission and one rule per code, the rule naming the
/// lowest role that satisfies it.
/// <para>
/// Ranks do the rest. <c>super_admin</c> outranks <c>staff</c> and the rule check is
/// <c>assigned.Rank &gt;= rule.Rank</c>, so a super admin satisfies every staff rule without a
/// second row — and a staff rule can never accidentally admit a super-admin-only area, because
/// the comparison only ever widens upward.
/// </para>
/// <para>
/// Every identifier is a literal generated once and committed. Computing them — from a hash of
/// the code, say — would make the seed depend on an algorithm, and changing that algorithm later
/// would silently orphan every row already in a production database.
/// </para>
/// </summary>
public static class AdminPermissionMatrix
{
    public const int StaffRank = 10;
    public const int SuperAdminRank = 20;

    public static readonly Guid StaffRoleId = Guid.Parse("7237C944-C6D2-4DCF-B0E4-ECF2768CE08D");
    public static readonly Guid SuperAdminRoleId = Guid.Parse("1A52E0A6-982A-4EAC-843D-CF357B081742");

    /// <summary>One row per <see cref="AdminPermissions"/> constant; a test asserts that.</summary>
    public static IReadOnlyList<MatrixRow> Rows { get; } =
    [
        Staff(AdminPermissions.CatalogManage, "Manage catalog",
            "456B4292-5990-4B68-8FD7-1A43193E2D38", "93883648-7F7F-4D32-B5FD-9EA6146F5FBF"),
        Staff(AdminPermissions.ImportsManage, "Manage imports",
            "638688A3-9A51-4D23-8EE0-58E2EFBFA773", "E71446DA-CAB8-4DB4-9CA9-D8407E505026"),
        Staff(AdminPermissions.CustomersManage, "Manage customers",
            "1B1BC629-D7BE-4AE9-8379-A4A96E01F2D4", "33A6CE58-A48F-45CC-A481-5DD1FFC44AF0"),
        Staff(AdminPermissions.OrdersManage, "Manage orders",
            "23C77D5C-58D0-49B5-818E-83A23D108D38", "9F51A166-39C3-4EB8-A0C7-D1B7F877FE5F"),
        Staff(AdminPermissions.PaymentsConfirm, "Confirm payments",
            "D71FAC8D-4FB0-4CDA-BD94-482E056A01C9", "F6C02D4A-1B22-4AE3-BAFF-141A8960E3F9"),

        Super(AdminPermissions.PartnersManage, "Manage partners",
            "37E59E3A-94FE-43D1-BF8F-D0F57CFC82D7", "769FA513-A0DB-42BC-8A67-0FCC5F6C3073"),
        Super(AdminPermissions.FxManage, "Manage FX rates",
            "A445CF70-2F1E-498A-8282-F1E2881E84BC", "9C44F926-9820-4E3D-89C5-18D07620A677"),
        Super(AdminPermissions.CurrenciesManage, "Manage currencies",
            "3E2384FC-8EB3-492B-BD4B-1519AA403015", "82BAE564-7381-47FD-B277-D5F232733ACB"),
        Super(AdminPermissions.SettingsManage, "Manage settings",
            "6E892F33-023E-429B-B697-4AD522A6615A", "AC294BDD-0EA0-4BCE-B96A-4CE936236978"),
        Super(AdminPermissions.RefundsManage, "Manage refunds",
            "740123BD-BAA3-4F9B-9E3F-4D0B960AAAB7", "7A376C6A-5026-4FB1-AE11-E1ABF946F0A9"),
        Super(AdminPermissions.AdminAccountsManage, "Manage admin accounts",
            "630E0097-BB6C-4587-A8E1-FD0A561CDF32", "EC3C722E-B2B4-4D5A-B9C9-04FBB5985862"),
        Super(AdminPermissions.AuditVerify, "Verify the audit log",
            "0A5E9B4C-3D71-4F28-9C60-5E8B2A1D7F43", "C1B47D82-6E95-4A30-8F17-2D9C6B5E8A14"),
    ];

    private static MatrixRow Staff(string code, string name, string permissionId, string ruleId) =>
        new(code, name, StaffRoleId, Guid.Parse(permissionId), Guid.Parse(ruleId));

    private static MatrixRow Super(string code, string name, string permissionId, string ruleId) =>
        new(code, name, SuperAdminRoleId, Guid.Parse(permissionId), Guid.Parse(ruleId));
}

public sealed record MatrixRow(string Code, string Name, Guid RoleId, Guid PermissionId, Guid RuleId);
