using EwaysVNext.Jobs;
using EwaysVNext.Jobs.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.Audit.Implementation;

public static class DependencyInjection
{
    public static IServiceCollection AddAudit(this IServiceCollection services)
    {
        services.TryAddScoped<IAuditTrail, EfAuditTrail>();
        services.TryAddScoped<IAuditVerifier, AuditVerifier>();

        services.AddJobHandler<AuditSealJobHandler, EmptyPayload>();
        services.AddRecurringJob(JobTypes.AuditSeal, TimeSpan.FromMinutes(1));

        return services;
    }
}
