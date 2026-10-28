using EwaysVNext.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.Modules.Catalog.Application.Categories;

public static class DependencyInjection
{
    public static IServiceCollection AddCategoryApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, SnowflakeIdGenerator>();
        services.AddScoped<Ports.Inbound.ICategoryService, Services.CategoryService>();
        return services;
    }
}
