using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Database;

public static class PersistenceDependencyInjection
{
    public static IServiceCollection AddUnifiedDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.AddDbContext<EwaysVNextDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString("Default");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.MigrationsAssembly(typeof(EwaysVNextDbContext).Assembly.FullName);
                    sql.MigrationsHistoryTable("__EFMigrationsHistory");
                    sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
                });
            }
            else if (isDevelopment)
            {
                options.UseInMemoryDatabase("EwaysVNext");
            }
            else
            {
                throw new InvalidOperationException(PersistenceDependencyInjectionMessages.ConnectionStringRequiredOutsideDevelopment);
            }

            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });

        services.AddHealthChecks().AddDbContextCheck<EwaysVNextDbContext>("Database", tags: ["ready"]);

        return services;
    }
}
