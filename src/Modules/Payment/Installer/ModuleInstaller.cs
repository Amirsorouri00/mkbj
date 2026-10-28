using EwaysVNext.Modules.Payment.Adapter.Outbound;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;
using EwaysVNext.Modules.Payment.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Payment;

public static class ModuleInstaller
{
    public static IServiceCollection AddPaymentModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddPaymentApplication();
        services.AddPaymentOutboundAdapters(config);
        services.AddParsianGateway(config);
        services.AddMellatGateway(config);
        return services;
    }
}
