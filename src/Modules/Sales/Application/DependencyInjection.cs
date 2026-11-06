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

        return services;
    }
}
