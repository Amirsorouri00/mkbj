using Mkbj.Modules.Admin.Application.Ports.Inbound;
using Mkbj.Modules.Admin.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Admin.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAdminApplication(this IServiceCollection services)
    {
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<ICustomer360Service, Customer360Service>();
        services.AddScoped<IOrder360Service, Order360Service>();
        services.AddScoped<IAuditVerificationService, AuditVerificationService>();

        return services;
    }
}
