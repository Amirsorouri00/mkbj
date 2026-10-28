using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Payment.Application.Ports.Outbound;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentOutboundAdapters(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<PaymentGatewayOptions>()
            .Bind(config.GetSection(PaymentGatewayOptions.SectionName))
            .Validate(o => o.LinkCreationTimeout.TotalMilliseconds >= 1 && o.LinkCreationTimeout.TotalMilliseconds <= uint.MaxValue - 1,
                "Payment gateway link creation timeout must be within the supported timer range (1 to 4294967294 milliseconds).")
            .Validate(o => o.FallbackProviders is not null && o.FallbackProviders.All(n => !string.IsNullOrWhiteSpace(n)),
                "Payment fallback provider names cannot be empty.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IIdGenerator, SnowflakeIdGenerator>();
        services.TryAddScoped<IPaymentGatewayProviderSelector, ConfiguredProviderSelector>();
        services.TryAddScoped<IGatewayCallExecutor, GatewayCallExecutor>();
        services.TryAddScoped<IPaymentGateway, PaymentGateway>();
        return services;
    }
}
