using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

namespace EwaysVNext.Modules.Payment.Application.Domain;

/// <summary>
/// Payment's contract for an already-created BankTransaction aggregate.
/// The transaction lifecycle creates it before calling the gateway subsystem.
/// Gateway integration pays this transaction; it never creates a replacement
/// aggregate, accepts an Order as its payment subject, or changes the amount.
/// </summary>
public interface IBankTransaction : IAggregateRoot
{
    long Id { get; }
    Money Amount { get; }
    /// <summary>The attempt retained after link creation; null until a payment link exists.</summary>
    GatewayAttempt? GatewayAttempt { get; }
}
