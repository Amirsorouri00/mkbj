using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Customers.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Customer.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerApplication(this IServiceCollection services)
    {
        services.AddScoped<ICustomerResolutionPort, CustomerResolutionService>();
        services.AddScoped<IWebsiteCustomerPort, WebsiteCustomerService>();
        services.AddScoped<ICustomerProfileService, CustomerProfileService>();

        return services;
    }
}
