using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Services;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions;

public static class DependencyInjection
{
    public static IServiceCollection AddCategoryAttributeExclusionApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, SnowflakeIdGenerator>();
        services.AddScoped<ICategoryAttributeExclusionService, CategoryAttributeExclusionService>();
        services.AddScoped<ICategoryAttributeExclusionQueryService, CategoryAttributeExclusionQueryService>();
        services.AddScoped<CategoryAttributeExclusionValidator>();

        return services;
    }
}
