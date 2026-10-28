using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Services;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Validation;
using EwaysVNext.Modules.Catalog.QueryApplication.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments;

public static class DependencyInjection
{
    public static IServiceCollection AddCategoryAttributeAssignmentApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, SnowflakeIdGenerator>();
        services.AddScoped<ICategoryAttributeAssignmentService, CategoryAttributeAssignmentService>();
        services.AddScoped<ICategoryAttributeAssignmentQueryService, CategoryAttributeAssignmentQueryService>();
        services.AddScoped<CategoryAttributeAssignmentValidator>();
        services.AddScoped<ReorderCategoryAttributeValidator>();

        return services;
    }
}
