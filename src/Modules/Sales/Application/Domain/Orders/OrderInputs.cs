using EwaysVNext.Modules.Sales.Application.Domain.Documents;

namespace EwaysVNext.Modules.Sales.Application.Domain.Orders;

/// <summary>
/// What a line's price was, and how it was arrived at.
/// </summary>
/// <remarks>
/// Copied onto the order, not referenced. Rates move and overrides are replaced; this is the
/// evidence for why the buyer was charged this number, and it has to survive both. The rate ids
/// point at append-only rows, so the snapshot stays resolvable rather than merely descriptive.
/// </remarks>
public sealed record PriceSnapshot(
    long ReferenceMinor,
    LinePriceSource Source,
    decimal? FxRate,
    long? FxRateId,
    long? FxOverrideId,
    DateTimeOffset? FxRateAt,
    string? FxProvenance,
    long BaseAmountMinor,
    string BaseCurrency);

/// <summary>
/// What the product was called when it was sold.
/// </summary>
/// <remarks>
/// A catalogue is edited constantly — titles reworded, categories moved, variants retired. An
/// invoice must keep describing what the buyer actually bought, so the description is frozen
/// here rather than joined from the catalogue at print time.
/// </remarks>
public sealed record VariantSnapshot(
    long VariantId,
    long ProductId,
    string Sku,
    string ProductTitle,
    string BrandName,
    IReadOnlyList<string> CategoryPath,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record OrderLineInput(
    long LineId, VariantSnapshot Variant, int Quantity, long UnitPriceMinor, PriceSnapshot Price);

/// <summary>A document number the caller has already allocated.</summary>
/// <remarks>
/// Allocated outside the aggregate because numbering is a gapless sequence shared across orders,
/// which no single aggregate owns.
/// </remarks>
public sealed record IssuedDocumentRef(long DocumentId, string Number);

public sealed record CompositionTraceInput(
    long Seed,
    string StrategyVersion,
    int Iterations,
    long DurationMs,
    int CandidatePoolSize,
    string AttemptsJson);

/// <summary>
/// Everything about a partner order that is settled before its lines are known.
/// </summary>
/// <remarks>
/// <c>RequestFingerprint</c> is what a retry is recognised by, and <c>CorrelationId</c> ties the
/// order to the request that created it — the one thread that leads from a partner's support
/// ticket back to a log line.
/// </remarks>
public sealed record PartnerOrderHeader(
    long OrderId,
    string Number,
    long PartnerId,
    string ExternalOrderId,
    string ExternalCustomerId,
    string RequestFingerprint,
    long CustomerId,
    string Currency,
    long RequestedTotalMinor,
    long UsdEquivalentMinor,
    string CorrelationId);

/// <summary>
/// How order and document numbers are written.
/// </summary>
/// <remarks>
/// Zero-padded to six digits so numbers sort lexicographically for as long as the padding holds,
/// and prefixed by kind so a number in an email or a bank reference says what it refers to
/// without context.
/// </remarks>
public static class OrderNumberFormat
{
    public static string Order(long sequence) => Format("ORD", sequence);

    public static string Document(DocumentType type, long sequence) => Format(
        type switch
        {
            DocumentType.ProForma => "PF",
            DocumentType.Commercial => "INV",
            DocumentType.CreditNote => "CN",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown document type."),
        },
        sequence);

    private static string Format(string prefix, long sequence) =>
        $"{prefix}-{sequence.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Everything about a website order that is settled before its lines are known.
/// </summary>
/// <remarks>
/// Shorter than a partner order's header, and the absences are the point: a website buyer has no
/// external order id to be idempotent against and no partner to bill. What makes a checkout
/// unique is the cart it emptied.
/// </remarks>
public sealed record WebsiteOrderHeader(
    long OrderId,
    string Number,
    long CustomerId,
    string Currency,
    long UsdEquivalentMinor,
    string CorrelationId);
