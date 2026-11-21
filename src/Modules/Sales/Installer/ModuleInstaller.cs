using Mkbj.Database;
using Mkbj.DependencyInjection;
using Mkbj.Modules.Sales.Adapter.Inbound.GraphQL;
using Mkbj.Modules.Sales.Adapter.Inbound.Rest;
using Mkbj.Modules.Sales.Adapter.Outbound;
using Mkbj.Modules.Sales.Application;
using Mkbj.Modules.Sales.Application.Composition;
using Mkbj.Modules.Sales.QueryApplication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Sales;

public static class ModuleInstaller
{
    /// <summary>
    /// Takes <see cref="IConfiguration"/> because Sales reads deployment-level limits — bulk
    /// parallelism, the bulk deadline and how long a candidate pool may be cached — that belong
    /// to the environment rather than to the seller's data.
    /// </summary>
    public static IServiceCollection AddSalesModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSalesApplication(configuration);
        services.AddSalesOutboundAdapters();
        services.AddSalesQueryApplication();
        services.AddSalesRestAdapter();
        services.AddSalesGraphQL();

        services
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddUnitOfWork<MkbjDbContext>()
            .AddEvents(typeof(CompositionEngine).Assembly);

        return services;
    }
}
