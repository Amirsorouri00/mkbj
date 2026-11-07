using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Sales.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the hexagon's own services. Filled in as each one arrives; it exists from the
    /// scaffold so the installer has one place to call rather than growing a list of its own.
    /// </summary>
    public static IServiceCollection AddSalesApplication(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SalesOptions>().BindConfiguration(SalesOptions.SectionName);

        services.AddScoped<ITaxPolicy, NonGstTaxPolicy>();
        services.AddScoped<LiveRequoter>();
        services.AddScoped<IPartnerOrderService, PartnerOrderService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IShopCustomerResolver, ShopCustomerResolver>();
        services.AddScoped<IInvoiceService, InvoiceService>();

        return services;
    }
}
