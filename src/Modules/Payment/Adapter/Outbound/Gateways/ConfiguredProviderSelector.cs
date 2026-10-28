using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;

internal sealed class ConfiguredProviderSelector : IPaymentGatewayProviderSelector
{
    private static readonly Error NotRegistered = new("PAYMENT_PROVIDER_NOT_REGISTERED", "The selected gateway is not registered.", 503);
    private static readonly Error Ambiguous = new("PAYMENT_PROVIDER_AMBIGUOUS", "Gateway provider names must be unique.", 503);

    private readonly Dictionary<string, Result<IPaymentGatewayProvider>> _registry = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOptionsSnapshot<PaymentGatewayOptions> _options;
    private readonly ILogger<ConfiguredProviderSelector> _logger;

    public ConfiguredProviderSelector(IEnumerable<IPaymentGatewayProvider> providers,
        IOptionsSnapshot<PaymentGatewayOptions> options, ILogger<ConfiguredProviderSelector> logger)
    {
        _options = options;
        _logger = logger;
        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Name))
                throw new InvalidOperationException("Gateway provider names cannot be empty.");
            // A duplicate name stays an error instead of silently picking one registration.
            if (!_registry.TryAdd(provider.Name, Result.Success(provider)))
                _registry[provider.Name] = Result.Failure<IPaymentGatewayProvider>(Ambiguous);
        }
    }

    public Result<LinkCreationPlan> SelectForLinkCreation(string? providerName)
    {
        var options = _options.Value;
        if (providerName is not null)
        {
            var selected = SelectRetained(providerName);
            return selected.IsFailure
                ? Result.Failure<LinkCreationPlan>(selected.Error!)
                : Result.Success(new LinkCreationPlan([selected.Value!], options.LinkCreationTimeout));
        }

        if (string.IsNullOrWhiteSpace(options.DefaultProvider))
            return Result.Failure<LinkCreationPlan>(PaymentGatewayErrors.NotConfigured);

        var providers = new List<IPaymentGatewayProvider>();
        foreach (var name in PriorityOrder(options))
        {
            var resolved = SelectRetained(name);
            if (resolved.IsSuccess)
                providers.Add(resolved.Value!);
            else
                _logger.LogWarning("Configured payment provider {Provider} is skipped ({ErrorCode})", name, resolved.Error!.Code);
        }

        return providers.Count == 0
            ? Result.Failure<LinkCreationPlan>(PaymentGatewayErrors.Exhausted)
            : Result.Success(new LinkCreationPlan(providers, options.LinkCreationTimeout));
    }

    public Result<IPaymentGatewayProvider> SelectRetained(string providerName) =>
        providerName is not null && _registry.TryGetValue(providerName, out var provider)
            ? provider
            : Result.Failure<IPaymentGatewayProvider>(NotRegistered);

    private static IEnumerable<string> PriorityOrder(PaymentGatewayOptions options) =>
        new[] { options.DefaultProvider! }
            .Concat(options.FallbackProviders)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
