using AuthorizationService.Abstractions.Authorization;
using AuthorizationService.Abstractions.Policies;
using AuthorizationService.Application.Abstractions;
using AuthorizationService.Application.Permissions;
using AuthorizationService.Application.Roles;
using AuthorizationService.Application.Scopes;
using AuthorizationService.Application.UserRoleScopes;
using AuthorizationService.Implementation.Handlers;
using AuthorizationService.Implementation.Persistence;
using AuthorizationService.Implementation.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuthorizationService.Implementation;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthorizationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AuthorizationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(DependencyInjectionMessages.ConnectionStringNotConfigured);
            }

            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(AuthorizationDbContext).Assembly.FullName);
                sql.MigrationsHistoryTable("__AuthorizationMigrationsHistory", "authorization");
                sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
            });
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AuthorizationDbContext>());

        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IScopeService, ScopeService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IUserRoleScopeService, UserRoleScopeService>();
        services.AddScoped<IAuthorizationCheckService, AuthorizationCheckService>();

        services.AddSingleton<IAuthorizationPolicyProvider, DynamicAuthorizationPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddHttpContextAccessor();

        return services;
    }
}
