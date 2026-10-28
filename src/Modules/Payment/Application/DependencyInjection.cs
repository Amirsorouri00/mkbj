using EwaysVNext.Modules.Payment.Application.Ports.Inbound;
using EwaysVNext.Modules.Payment.Application.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.Modules.Payment.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentApplication(this IServiceCollection services)
    {
        services.TryAddScoped<IPaymentGatewayService, PaymentGatewayService>();
        return services;
    }
}
