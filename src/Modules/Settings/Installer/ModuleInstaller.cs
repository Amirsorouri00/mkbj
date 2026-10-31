using EwaysVNext.Database;
using EwaysVNext.DependencyInjection;
using EwaysVNext.Modules.Settings.Adapter.Outbound;
using EwaysVNext.Modules.Settings.Application;
using EwaysVNext.Modules.Settings.Application.Settings.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Settings;

public static class ModuleInstaller
{
    public static IServiceCollection AddSettingsModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSettingsApplication(configuration);
        services.AddSettingsOutboundAdapters();

        var applicationAssembly = typeof(SellerProfile).Assembly;
        services
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddUnitOfWork<EwaysVNextDbContext>()
            .AddEvents(applicationAssembly);

        return services;
    }
}
