using EwaysVNext.Modules.Customer.QueryApplication.Customers;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Customer.QueryApplication;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerQueryApplication(this IServiceCollection services)
    {
        services.AddScoped<ICustomerShopQueries, CustomerShopQueries>();

        return services;
    }
}
