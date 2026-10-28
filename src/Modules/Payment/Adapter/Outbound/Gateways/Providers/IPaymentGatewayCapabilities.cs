using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

/// <summary>Implemented by a provider that can reverse an attempt with its bank.</summary>
public interface IPaymentReversalProvider
{
    Task<Result<GatewayReversalResult>> ReverseAsync(GatewayPaymentContext payment, CancellationToken ct);
}

/// <summary>Implemented by a provider that offers an authoritative inquiry of an attempt's outcome.</summary>
public interface IPaymentInquiryProvider
{
    Task<Result<GatewayInquiryObservation>> InquiryAsync(GatewayPaymentContext payment, CancellationToken ct);
}
