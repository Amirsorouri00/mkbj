using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

/// <summary>
/// Strategy implemented by each bank's outbound ACL adapter. Implementations must
/// be asynchronous, honor cancellation, and translate bank refusals into Result.
/// No method may mutate a transaction aggregate or start another provider.
/// Follow-up operations are optional capability interfaces on the same instance.
/// </summary>
public interface IPaymentGatewayProvider
{
    string Name { get; }
    Task<Result<GatewayLink>> CreateLinkAsync(GatewayLinkRequest request, CancellationToken ct);
    /// <summary>
    /// Apply the bank's documented callback checks and, only when the callback reports payment,
    /// confirm it with the bank server-side. A failed check is an error and the bank is not called.
    /// An unresolved confirmation is <see cref="GatewayPaymentState.Unknown"/>, never Failed.
    /// </summary>
    Task<Result<GatewayCallbackResult>> VerifyCallbackAsync(GatewayCallbackRequest request, CancellationToken ct);
}
