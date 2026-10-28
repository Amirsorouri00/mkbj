namespace EwaysVNext.Presentation.Endpoint.Security;

/// <summary>
/// Policy names. A policy answers "which realm, and broadly who" — it is the coarse gate. The
/// fine-grained question, "may this user do this particular thing", is a permission check on the
/// individual endpoint.
/// <para>
/// Both are applied, deliberately. Permission rows are editable data, so a misedited row could
/// hand staff something only a super admin should have; the realm policy is not editable from
/// inside the application and keeps that mistake from reaching a super-admin-only area.
/// </para>
/// </summary>
public static class AuthPolicies
{
    /// <summary>Backoffice realm, role <c>staff</c> or <c>super_admin</c>.</summary>
    public const string AdminStaff = "admin:staff";

    /// <summary>Backoffice realm, role <c>super_admin</c> only.</summary>
    public const string AdminSuper = "admin:super";

    /// <summary>Shop realm — an authenticated website customer.</summary>
    public const string ShopCustomer = "shop:customer";

    public const string PartnerPrefix = "partner:";

    /// <summary>The policy requiring one partner API-key scope, e.g. <c>partner:orders:create</c>.</summary>
    public static string Partner(string scope) => PartnerPrefix + scope;
}
