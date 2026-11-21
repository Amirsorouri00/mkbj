using System.Text.Json.Serialization;
namespace Mkbj.Modules.Admin.Application.Views;

/// <summary>
/// Everything the backoffice reads, in Admin's own words.
/// </summary>
/// <remarks>
/// <para>
/// These are local records, not the providing modules' DTOs, and the duplication is the point.
/// Admin composes four modules; if it passed their types through, a rename in Sales would ripple
/// into the backoffice GraphQL schema and out to whatever is drawing it. The gateways map field
/// by field so that a provider's change is a compile error in one file.
/// </para>
/// <para>
/// Nothing here is an aggregate and nothing here is writable. Admin has no database of its own
/// (ADR-0006) and <c>AdminArchitectureTests</c> fails the build if it acquires one.
/// </para>
/// </remarks>
public sealed record OrderTotalsRow(
    string Window, string Channel, string Currency, int OrderCount, long TotalMinor);

public sealed record CurrencyAmount(string Currency, long AmountMinor);

public sealed record PendingPayments(
    int Count, IReadOnlyList<CurrencyAmount> AmountsByCurrency, DateTimeOffset? OldestCreatedAt);

public sealed record CompositionWindow(
    string Window, int Attempts, int Succeeded, decimal SuccessRate, long? P95DurationMs);

public sealed record CompositionFailure(
    DateTimeOffset At,
    string ErrorCode,
    string Currency,
    long TargetMinor,
    long DurationMs,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long? PartnerId);

public sealed record SalesSnapshot(
    IReadOnlyList<OrderTotalsRow> OrderTotals,
    PendingPayments PendingWebsitePayments,
    CompositionWindow Composition24h,
    CompositionWindow Composition7d,
    IReadOnlyList<CompositionFailure> RecentFailures);

public sealed record FailureCodeCount(string ErrorCode, int Count);

/// <remarks>
/// The "7d" fields are named for the window an operator reads them in, not for the date the
/// query used. Sourcing reports them as "since", and the gateway maps one to the other.
/// </remarks>
public sealed record ImportSnapshot(
    int JobsQueued,
    int JobsRunning,
    int JobsPaused,
    int JobsFailed7d,
    int JobsCompleted7d,
    int RecordsNeedsReview,
    int RecordsValidUnpublished,
    int RecordsInvalid,
    int RecordsPublished7d);

public sealed record AdminDashboard(
    DateTimeOffset GeneratedAt,
    SalesSnapshot Sales,
    ImportSnapshot Imports,
    IReadOnlyList<FailureCodeCount> RecentFailureCodes);

public sealed record OrderStatusView(
    string Order,
    string Payment,
    string? PaymentSource,
    string Fulfillment,
    string? FulfillmentSource,
    string FulfillmentStatement,
    string Documents);

public sealed record OrderRow(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id,
    string Number,
    string Channel,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long? PartnerId,
    string? ExternalOrderId,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long CustomerId,
    string Currency,
    long TotalMinor,
    long RefundedMinor,
    OrderStatusView Status,
    DateTimeOffset CreatedAt);

/// <remarks>
/// <c>PdfPath</c> is built here rather than returned by Sales: it is the <b>admin</b> route, and
/// which route a reader may use is a property of who is reading, not of the document.
/// </remarks>
public sealed record DocumentView(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id,
    string Type,
    string Number,
    DateTimeOffset IssuedAt,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long? RelatedDocumentId,
    string PdfPath);

public sealed record PaymentView(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id,
    string Source,
    string? Method,
    string? Reference,
    Guid? VerifiedBy,
    DateTimeOffset RecordedAt,
    long AmountMinor);

public sealed record RefundView(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id,
    long AmountMinor,
    string Reason,
    Guid RecordedBy,
    DateTimeOffset RecordedAt,
    string? ExternalReference,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long CreditNoteDocumentId);

/// <remarks>
/// <c>LineCount</c> rather than the lines. An order sheet is a summary an operator scans; the
/// lines are a click away on the Sales detail route, and carrying a hundred of them into every
/// 360 view would make the common case pay for the rare one.
/// </remarks>
public sealed record OrderSheet(
    OrderRow Summary,
    string? ExternalCustomerId,
    long UsdEquivalentMinor,
    string CorrelationId,
    int LineCount,
    IReadOnlyList<PaymentView> Payments,
    IReadOnlyList<DocumentView> Documents,
    IReadOnlyList<RefundView> Refunds);

public sealed record AddressView(
    string Line1, string? Line2, string City, string PostalCode, string CountryIso2);

public sealed record ContactChangeEntry(
    string Field,
    string? OldValue,
    string? NewValue,
    DateTimeOffset ChangedAt,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long? SourcePartnerId,
    string CorrelationId);

public sealed record CustomerProfile(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id,
    string Kind,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long? PartnerId,
    string? ExternalCustomerId,
    string? Email,
    string? Phone,
    string? FullName,
    string? CompanyName,
    string Status,
    bool EmailVerified,
    AddressView? BillingAddress,
    string? RegistrationNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ContactChangeEntry> ContactHistory);

public sealed record Customer360(CustomerProfile Customer, IReadOnlyList<OrderRow> RecentOrders);

/// <remarks>
/// The customer is nullable because an order outlives the record of who bought it in exactly one
/// case worth handling: a partner-placed order whose customer row was later removed. The order
/// is still a real sale and must still be readable.
/// </remarks>
public sealed record Order360(OrderSheet Order, CustomerProfile? Customer);

public sealed record AuditVerificationView(
    bool Intact,
    long SealedCount,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long? FirstMismatchEventId,
    long UnsealedBelowWatermark,
    DateTimeOffset VerifiedAt);
