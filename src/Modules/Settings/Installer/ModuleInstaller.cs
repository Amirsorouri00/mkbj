using Mkbj.Database;
using Mkbj.DependencyInjection;
using Mkbj.Modules.Settings.Adapter.Inbound.GraphQL;
using Mkbj.Modules.Settings.Adapter.Inbound.Rest;
using Mkbj.Modules.Settings.Adapter.Outbound;
using Mkbj.Modules.Settings.Application;
using Mkbj.Modules.Settings.QueryApplication;
using Mkbj.Modules.Settings.Application.Settings.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Settings;

public static class ModuleInstaller
{
    public static IServiceCollection AddSettingsModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSettingsApplication(configuration);
        services.AddSettingsOutboundAdapters();
        services.AddSettingsRestAdapter();
        services.AddSettingsGraphQL();
        services.AddSettingsQueryApplication();

        var applicationAssembly = typeof(SellerProfile).Assembly;
        services
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddUnitOfWork<MkbjDbContext>()
            .AddEvents(applicationAssembly);

        return services;
    }
}
