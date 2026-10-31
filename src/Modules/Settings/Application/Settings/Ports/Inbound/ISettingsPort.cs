namespace EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;

public sealed record SellerSnapshot(
    string LegalName,
    string Uen,
    string Address,
    string Email,
    bool GstRegistered,
    string InvoiceFooter,
    IReadOnlyList<string> RequiredBuyerFields);

/// <summary>
/// The wording in force. <c>Id</c> is the version row's id, which an invoice stores so it can say
/// which wording the customer was actually given.
/// </summary>
public sealed record PaymentInstructionSnapshot(long Id, int Version, string Text);

public sealed record CommerceLimitsSnapshot(
    long MinUsdMinor,
    long MaxUsdMinor,
    int BulkDefaultMax,
    int CompositionDeadlineMs,
    int CompositionMaxLines,
    TimeSpan FxMaxRateAge);

/// <summary>
/// What every other module reads from Settings. Read-only on purpose: changing settings is an
/// admin act with its own service, and a consumer that could write them could change the seller's
/// invoice details as a side effect of placing an order.
/// </summary>
public interface ISettingsPort
{
    Task<SellerSnapshot> GetSellerAsync(CancellationToken ct = default);

    Task<PaymentInstructionSnapshot> GetActivePaymentInstructionAsync(CancellationToken ct = default);

    Task<CommerceLimitsSnapshot> GetCommerceLimitsAsync(CancellationToken ct = default);
}
