namespace EwaysVNext.Modules.Settings.QueryApplication;

public sealed record SellerView(
    string LegalName,
    string Uen,
    string Address,
    string Email,
    bool GstRegistered,
    string InvoiceFooter,
    IReadOnlyList<string> RequiredBuyerFields);

public sealed record PaymentInstructionVersionView(
    long Id,
    int Version,
    string Text,
    bool IsActive,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt);

/// <summary>
/// Amounts are two-decimal strings on the wire, not numbers: JSON numbers are doubles in most
/// clients, and a price that arrives as 24.999999999999996 is a rounding argument nobody wins.
/// </summary>
public sealed record CommerceLimitsView(
    string MinUsd,
    string MaxUsd,
    int BulkDefaultMax,
    int CompositionDeadlineMs,
    int CompositionMaxLines);
