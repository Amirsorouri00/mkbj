using System.Reflection;
using EwaysVNext.Core.Application;
using EwaysVNext.Core.Application.FileTypeDetection;
using EwaysVNext.Core.Implementation;
using EwaysVNext.Core.Implementation.FileTypeDetection;
using EwaysVNext.Database;
using EwaysVNext.Domain.Common;
using EwaysVNext.Events.Application;
using EwaysVNext.Events.Implementation;
using EwaysVNext.Persistence;
using EwaysVNext.Persistence.Audit;
using EwaysVNext.Persistence.SoftDelete;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddEvents(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.AddScoped<IDomainEventBus, DomainEventBus>();
        services.Scan(scan => scan
            .FromAssemblies(assemblies)
            .AddClasses(classes => classes.AssignableTo(typeof(IDomainEventHandler<>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());
        return services;
    }

    public static IServiceCollection AddUserProfileService(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserProfileService, UserProfileService>();
        return services;
    }

    public static IServiceCollection AddFileTypeDetection(this IServiceCollection services)
    {
        services.TryAddSingleton<IFileTypeDetector, FileTypeDetector>();
        return services;
    }

    public static IServiceCollection AddPersistenceInterceptors(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, SnowflakeIdGenerator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISaveChangesInterceptor, AuditInterceptor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISaveChangesInterceptor, SoftDeleteInterceptor>());
        return services;
    }

    public static IServiceCollection AddUnitOfWork<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork<TDbContext>>();
        return services;
    }
}
