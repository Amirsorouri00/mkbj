using EwaysVNext.Presentation.Endpoint;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Pricing.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    public static IServiceCollection AddPricingRestAdapter(this IServiceCollection services)
    {
        services.AddEndpoints(typeof(ShopCurrencyEndpoints).Assembly);
        return services;
    }
}
