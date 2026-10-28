using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.GatewayTests;

internal static class ProviderOperations
{
    // A provider without the capability fails the test with InvalidCastException.
    public static Task<Result<GatewayReversalResult>> ReverseAsync(this IPaymentGatewayProvider provider,
        GatewayPaymentContext payment, CancellationToken ct) =>
        ((IPaymentReversalProvider)provider).ReverseAsync(payment, ct);
}
