namespace AuthorizationService.Abstractions.Policies;

/// <summary>
/// The backoffice permission codes of spec §6.1. The split is the product's, not a technical one:
/// the first five are the shop's day-to-day running, and the rest either move money, change what
/// the business charges, or hand out authority — so they belong to a super admin alone.
/// <para>
/// A code here is only a name. The rows that decide who holds it live in
/// <c>AdminPermissionMatrix</c> and are seeded as editable data, which is why an endpoint is
/// gated by a role policy <i>and</i> a permission: a permission row can be edited, a realm role
/// cannot.
/// </para>
/// </summary>
public static class AdminPermissions
{
    // Staff and super admin.
    public const string CatalogManage = "catalog.manage";
    public const string ImportsManage = "imports.manage";
    public const string CustomersManage = "customers.manage";
    public const string OrdersManage = "orders.manage";
    public const string PaymentsConfirm = "payments.confirm";

    // Super admin only.
    public const string PartnersManage = "partners.manage";
    public const string FxManage = "fx.manage";
    public const string CurrenciesManage = "currencies.manage";
    public const string SettingsManage = "settings.manage";
    public const string RefundsManage = "refunds.manage";
    public const string AdminAccountsManage = "admin-accounts.manage";

    /// <summary>
    /// Reading the audit chain's verdict.
    /// </summary>
    /// <remarks>
    /// Its own permission, and super admin only. "Intact: false" is evidence that the record of
    /// who did what has been edited, and that is not a staff-level conversation.
    /// </remarks>
    public const string AuditVerify = "audit.verify";
}
