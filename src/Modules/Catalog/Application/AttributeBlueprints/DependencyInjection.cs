using Mkbj.Domain.Common;
using Mkbj.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;
using Mkbj.Modules.Catalog.Application.AttributeBlueprints.Services;
using Mkbj.Modules.Catalog.Application.AttributeBlueprints.Validation;
using Mkbj.Modules.Catalog.QueryApplication.Abstractions.AttributeBlueprints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mkbj.Modules.Catalog.Application.AttributeBlueprints;

public static class DependencyInjection
{
    public static IServiceCollection AddAttributeBlueprintApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, SnowflakeIdGenerator>();
        services.AddScoped<IAttributeBlueprintService, AttributeBlueprintService>();
        services.AddScoped<IAttributeBlueprintQueryService, AttributeBlueprintQueryService>();
        services.AddScoped<IMeasurementUnitQueryService, MeasurementUnitQueryService>();

        services.AddScoped<CreateAttributeBlueprintValidator>();
        services.AddScoped<RenameAttributeBlueprintValidator>();
        services.AddScoped<ChangeAttributeBlueprintDataTypeValidator>();
        services.AddScoped<AttributeBlueprintIdValidator>();
        services.AddScoped<AttributeBlueprintOptionInputValidator>();
        services.AddScoped<AttributeBlueprintOptionIdsValidator>();
        services.AddScoped<UpdateAttributeBlueprintValidator>();

        return services;
    }
}
