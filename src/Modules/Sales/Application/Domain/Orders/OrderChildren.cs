using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.Events;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;

namespace EwaysVNext.Modules.Sales.Application.Domain.Orders;

/// <summary>
/// One sold line, with everything needed to explain it years later.
/// </summary>
/// <remarks>
/// <c>DeviationBps</c> and <c>BandEnforced</c> are stored rather than recomputed: the band rule
/// may change, and what matters on an old order is that the price was inside the band <b>as it
/// stood when the order was placed</b>.
/// </remarks>
public sealed class OrderLine : Entity<long>
{
    private OrderLine() { }

    internal OrderLine(OrderLineInput input, int lineNo)
    {
        Id = input.LineId;
        LineNo = lineNo;
        VariantId = input.Variant.VariantId;
        ProductId = input.Variant.ProductId;
        Sku = input.Variant.Sku;
        ProductTitle = input.Variant.ProductTitle;
        BrandName = input.Variant.BrandName;
        CategoryPath = input.Variant.CategoryPath;
        Attributes = input.Variant.Attributes;
        Quantity = input.Quantity;
        UnitPriceMinor = input.UnitPriceMinor;
        LineTotalMinor = checked(input.Quantity * input.UnitPriceMinor);
        Price = input.Price;

        // Truncated towards zero, so a deviation never reads as larger than it was.
        DeviationBps = (int)((input.UnitPriceMinor - input.Price.ReferenceMinor) * 10_000
            / input.Price.ReferenceMinor);
        BandEnforced = true;
    }

    public int LineNo { get; private set; }
    public long VariantId { get; private set; }
    public long ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string ProductTitle { get; private set; } = string.Empty;
    public string BrandName { get; private set; } = string.Empty;
    public IReadOnlyList<string> CategoryPath { get; private set; } = [];
    public IReadOnlyDictionary<string, string> Attributes { get; private set; } =
        new Dictionary<string, string>();

    public int Quantity { get; private set; }
    public long UnitPriceMinor { get; private set; }
    public long LineTotalMinor { get; private set; }
    public PriceSnapshot Price { get; private set; } = null!;

    /// <summary>How far the unit price sat from the catalogue's, in basis points.</summary>
    public int DeviationBps { get; private set; }

    /// <summary>Whether the ±5% band was checked when this line was created.</summary>
    public bool BandEnforced { get; private set; }
}

/// <summary>
/// One record that money moved, and on whose word.
/// </summary>
/// <remarks>
/// Append-only by intent: a payment that turns out to be wrong is corrected by a refund, never
/// by editing the record of it.
/// </remarks>
public sealed class PaymentRecord : Entity<long>
{
    private PaymentRecord() { }

    internal PaymentRecord(
        long id, PaymentSource source, long amountMinor, DateTimeOffset recordedAt,
        string? method = null, string? reference = null, Guid? verifiedBy = null)
    {
        Id = id;
        Source = source;
        AmountMinor = amountMinor;
        RecordedAt = recordedAt;
        Method = method;
        Reference = reference;
        VerifiedBy = verifiedBy;
    }

    public PaymentSource Source { get; private set; }
    public string? Method { get; private set; }
    public string? Reference { get; private set; }

    /// <summary>The admin who confirmed it, where one did.</summary>
    public Guid? VerifiedBy { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
    public long AmountMinor { get; private set; }
}

/// <summary>
/// A document that was issued, with the content it was issued with.
/// </summary>
/// <remarks>
/// The content is a frozen snapshot and the PDF is addressed by object key, so reprinting is a
/// fetch rather than a re-render. A re-render would quietly produce a different document as soon
/// as anything upstream changed.
/// </remarks>
public sealed class OrderDocument : Entity<long>
{
    private OrderDocument() { }

    internal OrderDocument(
        long id, DocumentType type, string number, DateTimeOffset issuedAt,
        DocumentContent content, string pdfObjectKey, long? relatedDocumentId = null)
    {
        Id = id;
        Type = type;
        Number = number;
        IssuedAt = issuedAt;
        Content = content;
        PdfObjectKey = pdfObjectKey;
        RelatedDocumentId = relatedDocumentId;
    }

    public DocumentType Type { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public DocumentContent Content { get; private set; } = null!;

    /// <summary>The invoice a credit note is against.</summary>
    public long? RelatedDocumentId { get; private set; }

    public string PdfObjectKey { get; private set; } = string.Empty;
}

/// <summary>Money going back, and why.</summary>
public sealed class Refund : Entity<long>
{
    private Refund() { }

    internal Refund(
        long id, long amountMinor, string reason, Guid recordedBy, DateTimeOffset recordedAt,
        long creditNoteDocumentId, string? externalReference = null)
    {
        Id = id;
        AmountMinor = amountMinor;
        Reason = reason;
        RecordedBy = recordedBy;
        RecordedAt = recordedAt;
        CreditNoteDocumentId = creditNoteDocumentId;
        ExternalReference = externalReference;
    }

    public long AmountMinor { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid RecordedBy { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public string? ExternalReference { get; private set; }
    public long CreditNoteDocumentId { get; private set; }
}

/// <summary>
/// How this order's basket was composed, kept so it can be reproduced.
/// </summary>
/// <remarks>
/// The seed and strategy version are the reproduction recipe — the engine is pure, so they are
/// enough. The attempt notes are for explaining a near miss to whoever asks why the basket looks
/// the way it does.
/// </remarks>
public sealed class CompositionTrace
{
    private CompositionTrace() { }

    internal CompositionTrace(CompositionTraceInput input)
    {
        Seed = input.Seed;
        StrategyVersion = input.StrategyVersion;
        Iterations = input.Iterations;
        DurationMs = input.DurationMs;
        CandidatePoolSize = input.CandidatePoolSize;
        Attempts = input.AttemptsJson;
    }

    public long Seed { get; private set; }
    public string StrategyVersion { get; private set; } = string.Empty;
    public int Iterations { get; private set; }
    public long DurationMs { get; private set; }
    public int CandidatePoolSize { get; private set; }
    public string Attempts { get; private set; } = string.Empty;
}

public sealed record PartnerOrderCreatedDomainEvent(long OrderId, long PartnerId) : DomainEvent;
