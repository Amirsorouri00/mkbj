using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;

/// <summary>Rejects incomplete sale requests for every gateway, then applies the provider's own rules and mapping.</summary>
internal abstract class GatewaySaleRequestFactoryBase<TRequest>
{
    public Result<TRequest> Create(GatewayLinkRequest request)
    {
        if (request is null || request.RequestId <= 0 || request.BankTransactionId <= 0
            || request.Amount is null || request.CallbackUrl is not { IsAbsoluteUri: true })
            return Result.Failure<TRequest>(PaymentGatewayErrors.InvalidRequest);

        return CreateProviderRequest(request);
    }

    protected abstract Result<TRequest> CreateProviderRequest(GatewayLinkRequest request);
}
