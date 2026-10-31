using EwaysVNext.Database;
using EwaysVNext.DependencyInjection;
using EwaysVNext.Jobs;
using EwaysVNext.Jobs.Implementation;
using EwaysVNext.Modules.Partners.Adapter.Inbound.Rest;
using EwaysVNext.Modules.Partners.Adapter.Inbound.Rest.Usage;
using EwaysVNext.Modules.Partners.Adapter.Outbound;
using EwaysVNext.Modules.Partners.Adapter.Outbound.Usage;
using EwaysVNext.Modules.Partners.Application;
using EwaysVNext.Modules.Partners.Application.Partners.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EwaysVNext.Modules.Partners;

public static class ModuleInstaller
{
    public static IServiceCollection AddPartnersModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPartnersApplication(configuration);
        services.AddPartnersOutboundAdapters(configuration);
        services.AddPartnersRestAdapter();

        services.AddJobHandler<UsagePruneJobHandler, UsagePrunePayload>();
        services.AddRecurringJob(JobTypes.UsagePrune, TimeSpan.FromDays(1));
        services.AddHostedService<ApiKeyUsageFlushService>();

        var applicationAssembly = typeof(Partner).Assembly;
        services
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddUnitOfWork<EwaysVNextDbContext>()
            .AddEvents(applicationAssembly);

        return services;
    }

    /// <summary>
    /// Must sit after <c>UseAuthentication</c> — there is no partner to attribute a call to
    /// before that — and before <c>UseRateLimiter</c>, so refused requests are recorded too.
    /// </summary>
    public static IApplicationBuilder UsePartnersModule(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<PartnerApiUsageMiddleware>();
    }
}
