namespace EwaysVNext.Core.Application.Security;

/// <summary>
/// The two backoffice roles (spec §6.1) and the two realm names.
/// <para>
/// There are deliberately only two. Staff run the shop day to day; a super admin alone may touch
/// partners, FX, currencies, settings, refunds and other admin accounts — the operations where a
/// mistake costs money or hands out authority. A third role in between would need a reason the
/// product does not yet have.
/// </para>
/// </summary>
public static class AdminRoles
{
    public const string SuperAdmin = "super_admin";
    public const string Staff = "staff";

    /// <summary>The customer-facing realm. A role minted here is never backoffice authority.</summary>
    public const string ShopRealm = "Shop";

    public const string BackofficeRealm = "Backoffice";
}
