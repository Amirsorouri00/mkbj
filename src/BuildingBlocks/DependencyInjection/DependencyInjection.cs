using System.Reflection;
using Mkbj.Core.Application;
using Mkbj.Core.Application.FileTypeDetection;
using Mkbj.Core.Implementation;
using Mkbj.Core.Implementation.FileTypeDetection;
using Mkbj.Database;
using Mkbj.Domain.Common;
using Mkbj.Events.Application;
using Mkbj.Events.Implementation;
using Mkbj.Persistence;
using Mkbj.Persistence.Audit;
using Mkbj.Persistence.SoftDelete;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mkbj.DependencyInjection;

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
        // TryAdd: several modules call this, and a second registration would give one request two
        // correlation contexts.
        services.TryAddScoped<ICorrelationContext, CorrelationContext>();
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
