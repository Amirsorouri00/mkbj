using System.Text.Json;
using EwaysVNext.Modules.Sales.QueryApplication.Partner;

namespace EwaysVNext.Modules.Sales.QueryApplication.Admin;

public enum AdminChannelFilter
{
    Website,
    PartnerApi,
}

/// <remarks>
/// Keyset paging, as everywhere else here: an offset page shifts under an administrator while
/// orders keep arriving, and the row they were about to click moves to the previous page.
/// </remarks>
public sealed record AdminOrderFilter(
    AdminChannelFilter? Channel = null,
    string? OrderStatus = null,
    string? PaymentStatus = null,
    string? Currency = null,
    long? CustomerId = null,
    long? PartnerId = null,
    string? ExternalOrderId = null,
    string? Number = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    long? Cursor = null,
    int PageSize = 50);

public sealed record AdminOrderListItemDto(
    long Id,
    string Number,
    string Channel,
    long? PartnerId,
    string? ExternalOrderId,
    long CustomerId,
    string Currency,
    long TotalMinor,
    long RefundedMinor,
    OrderStatusDto Status,
    DateTimeOffset CreatedAt);

public sealed record AdminPaymentDto(
    long Id,
    string Source,
    string? Method,
    string? Reference,
    Guid? VerifiedBy,
    DateTimeOffset RecordedAt,
    long AmountMinor);

public sealed record AdminRefundDto(
    long Id,
    long AmountMinor,
    string Reason,
    Guid RecordedBy,
    DateTimeOffset RecordedAt,
    string? ExternalReference,
    long CreditNoteDocumentId);

/// <remarks>
/// The seed and strategy version are the reproduction recipe: the engine is pure, so an
/// administrator asked "why these products?" can replay the exact basket years later.
/// </remarks>
public sealed record AdminCompositionTraceDto(
    long Seed,
    string StrategyVersion,
    int Iterations,
    long DurationMs,
    int CandidatePoolSize,
    JsonElement Attempts);

public sealed record AdminOrderDetailDto(
    AdminOrderListItemDto Summary,
    string? ExternalCustomerId,
    long UsdEquivalentMinor,
    string CorrelationId,
    JsonElement Buyer,
    IReadOnlyList<OrderLineDto> Lines,
    IReadOnlyList<AdminPaymentDto> Payments,
    IReadOnlyList<DocumentDto> Documents,
    IReadOnlyList<AdminRefundDto> Refunds,
    AdminCompositionTraceDto? CompositionTrace);

/// <remarks>Cancelled orders are excluded: they are not revenue and never were.</remarks>
public sealed record ChannelCurrencyTotalsDto(
    string Window, string Channel, string Currency, int OrderCount, long TotalMinor);

public sealed record CurrencyAmountDto(string Currency, long AmountMinor);

/// <remarks>
/// <c>OldestCreatedAt</c> is the number that matters operationally: it says how long somebody
/// has been waiting for an administrator to look at their payment.
/// </remarks>
public sealed record PendingWebsitePaymentsDto(
    int Count, IReadOnlyList<CurrencyAmountDto> AmountsByCurrency, DateTimeOffset? OldestCreatedAt);

/// <remarks>
/// <c>P95DurationMs</c> is null when nothing was attempted, rather than zero: a window with no
/// compositions has no 95th percentile, and reporting zero would read as "instant".
/// </remarks>
public sealed record CompositionStatsDto(
    string Window, int Attempts, int Succeeded, decimal SuccessRate, long? P95DurationMs);

public sealed record CompositionFailureDto(
    DateTimeOffset At,
    string ErrorCode,
    string Currency,
    long TargetMinor,
    long DurationMs,
    long? PartnerId);

public sealed record SalesDashboardDto(
    IReadOnlyList<ChannelCurrencyTotalsDto> OrderTotals,
    PendingWebsitePaymentsDto PendingWebsitePayments,
    CompositionStatsDto Composition24h,
    CompositionStatsDto Composition7d,
    IReadOnlyList<CompositionFailureDto> RecentFailures);
