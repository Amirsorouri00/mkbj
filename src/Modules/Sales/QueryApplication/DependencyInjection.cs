using Mkbj.Modules.Sales.QueryApplication.Admin;
using Mkbj.Modules.Sales.QueryApplication.Partner;
using Mkbj.Modules.Sales.QueryApplication.Shop;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Sales.QueryApplication;

public static class DependencyInjection
{
    public static IServiceCollection AddSalesQueryApplication(this IServiceCollection services)
    {
        services.AddScoped<IPartnerOrderQueries, PartnerOrderQueries>();
        services.AddScoped<IShopOrderQueries, ShopOrderQueries>();
        services.AddScoped<ISalesAdminQueries, SalesAdminQueries>();
        services.AddScoped<IPartnerRequestQueries, PartnerRequestQueries>();

        return services;
    }
}
