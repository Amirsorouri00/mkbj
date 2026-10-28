using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;

/// <summary>The single owner of provider selection: configuration, priority and the provider registry.</summary>
public interface IPaymentGatewayProviderSelector
{
    /// <summary>
    /// An explicit name selects exactly that provider, never a fallback. A null name selects the
    /// configured default then fallbacks, de-duplicated; unregistered names are skipped.
    /// </summary>
    Result<LinkCreationPlan> SelectForLinkCreation(string? providerName);

    /// <summary>The provider an attempt was created with. Never consults priority configuration.</summary>
    Result<IPaymentGatewayProvider> SelectRetained(string providerName);
}

/// <summary>Providers to try in order, and how long to wait for each to create the link.</summary>
public sealed record LinkCreationPlan(IReadOnlyList<IPaymentGatewayProvider> Providers, TimeSpan Timeout);
