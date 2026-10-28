namespace EwaysVNext.Core.Application;

/// <summary>
/// The scopes a partner API key can carry (PRD §9). This list is closed: a scope string that is
/// not here is dropped when the principal is built, so a forged or stale token cannot widen its
/// own permissions by inventing one.
/// </summary>
public static class PartnerScopes
{
    public const string CustomersRead = "customers:read";
    public const string CustomersWrite = "customers:write";
    public const string OrdersCreate = "orders:create";
    public const string OrdersRead = "orders:read";
    public const string InvoicesRead = "invoices:read";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        CustomersRead, CustomersWrite, OrdersCreate, OrdersRead, InvoicesRead,
    };
}
