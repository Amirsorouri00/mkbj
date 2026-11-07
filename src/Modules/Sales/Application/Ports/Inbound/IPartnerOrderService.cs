using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Application.Ports.Inbound;

/// <summary>
/// One partner order request: an amount, not a basket.
/// </summary>
/// <remarks>
/// The partner says what it wants to spend and the system composes real SKUs totalling it
/// exactly. <c>ExternalOrderId</c> is the partner's own numbering and doubles as the idempotency
/// key, so a retry after a dropped connection returns the original order rather than buying
/// twice.
/// </remarks>
public sealed record CreatePartnerOrderCommand(
    string ExternalOrderId,
    string ExternalCustomerId,
    long AmountMinor,
    string Currency,
    PartnerContact? Contact);

/// <remarks>
/// <c>Replayed</c> distinguishes "we made this now" from "you already had this", which a partner
/// reconciling its own ledger needs. <c>Warnings</c> carries what went wrong without
/// invalidating the order — a contact sync that failed, for instance.
/// </remarks>
public sealed record PartnerOrderCreated(
    long OrderId, bool Replayed, IReadOnlyList<string> Warnings);

public enum BulkItemStatus
{
    Created = 1,
    Replayed = 2,
    Failed = 3,
}

public sealed record BulkItem(int Index, CreatePartnerOrderCommand Command);

public sealed record BulkItemOutcome(
    int Index,
    string ExternalOrderId,
    BulkItemStatus Status,
    long? OrderId,
    Error? Error,
    IReadOnlyList<string> Warnings);

public interface IPartnerOrderService
{
    Task<Result<PartnerOrderCreated>> CreateAsync(
        PartnerPrincipal principal, CreatePartnerOrderCommand command, string correlationId,
        CancellationToken ct = default);

    Task<IReadOnlyList<BulkItemOutcome>> CreateBulkAsync(
        PartnerPrincipal principal, IReadOnlyList<BulkItem> items, string correlationId,
        CancellationToken ct = default);
}
