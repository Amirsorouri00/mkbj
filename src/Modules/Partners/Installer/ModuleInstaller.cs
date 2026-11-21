using Mkbj.Database;
using Mkbj.DependencyInjection;
using Mkbj.Jobs;
using Mkbj.Jobs.Implementation;
using Mkbj.Modules.Partners.Adapter.Inbound.GraphQL;
using Mkbj.Modules.Partners.Adapter.Inbound.Rest;
using Mkbj.Modules.Partners.Adapter.Inbound.Rest.Usage;
using Mkbj.Modules.Partners.Adapter.Outbound;
using Mkbj.Modules.Partners.Adapter.Outbound.Usage;
using Mkbj.Modules.Partners.Application;
using Mkbj.Modules.Partners.QueryApplication;
using Mkbj.Modules.Partners.Application.Partners.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mkbj.Modules.Partners;

public static class ModuleInstaller
{
    public static IServiceCollection AddPartnersModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPartnersApplication(configuration);
        services.AddPartnersOutboundAdapters(configuration);
        services.AddPartnersRestAdapter();
        services.AddPartnersGraphQL();
        services.AddPartnersQueryApplication();

        services.AddJobHandler<UsagePruneJobHandler, UsagePrunePayload>();
        services.AddRecurringJob(JobTypes.UsagePrune, TimeSpan.FromDays(1));
        services.AddHostedService<ApiKeyUsageFlushService>();

        var applicationAssembly = typeof(Partner).Assembly;
        services
            .AddPersistenceInterceptors()
            .AddUserProfileService()
            .AddUnitOfWork<MkbjDbContext>()
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
