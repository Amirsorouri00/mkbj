using EwaysVNext.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.Modules.Catalog.Application.Products;

public static class DependencyInjection
{
    /// <summary>
    /// The product hexagon's own registrations. It holds only the clock today: the write services
    /// that used to live here were built around a product that carried its own price, and pricing
    /// has since moved to its own module. Task 10 rebuilds the admin side against the new shape.
    /// </summary>
    public static IServiceCollection AddProductApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        return services;
    }
}
