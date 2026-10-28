using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.Audit.Implementation;

public static class DependencyInjection
{
    public static IServiceCollection AddAudit(this IServiceCollection services)
    {
        services.TryAddScoped<IAuditTrail, EfAuditTrail>();
        return services;
    }
}
