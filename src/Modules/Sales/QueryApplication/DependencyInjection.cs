using EwaysVNext.Modules.Sales.QueryApplication.Partner;
using EwaysVNext.Modules.Sales.QueryApplication.Shop;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sales.QueryApplication;

public static class DependencyInjection
{
    public static IServiceCollection AddSalesQueryApplication(this IServiceCollection services)
    {
        services.AddScoped<IPartnerOrderQueries, PartnerOrderQueries>();
        services.AddScoped<IShopOrderQueries, ShopOrderQueries>();

        return services;
    }
}
