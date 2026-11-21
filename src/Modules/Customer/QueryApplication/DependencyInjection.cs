using Mkbj.Modules.Customer.QueryApplication.Customers;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Customer.QueryApplication;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerQueryApplication(this IServiceCollection services)
    {
        services.AddScoped<ICustomerShopQueries, CustomerShopQueries>();
        services.AddScoped<ICustomerAdminQueries, CustomerAdminQueries>();

        return services;
    }
}
