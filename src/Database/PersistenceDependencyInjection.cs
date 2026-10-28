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
                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(EwaysVNextDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public");
                    npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
                }).UseSnakeCaseNamingConvention();
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

        // Jobs and Audit (Tasks 10-13) reach the one context as a plain DbContext, so neither
        // building block has to reference EwaysVNext.Database.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<EwaysVNextDbContext>());

        services.AddHealthChecks().AddDbContextCheck<EwaysVNextDbContext>("Database", tags: ["ready"]);

        return services;
    }
}
