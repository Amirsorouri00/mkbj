using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AuthorizationService.Implementation.Persistence;

public sealed class AuthorizationDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<AuthorizationDbContext>
{
    private const string HostUserSecretsId = "1d39ed74-2f0c-4885-9395-409036740084";

    public AuthorizationDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(HostUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        // See EwaysVNextDbContextDesignTimeFactory: `migrations add` never connects.
        var connectionString = configuration.GetConnectionString("Default") is { Length: > 0 } configured
            ? configured
            : "Host=localhost;Database=eways_design;Username=design;Password=design";

        var options = new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AuthorizationDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__AuthorizationMigrationsHistory", "authorization");
                })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AuthorizationDbContext(options);
    }
}
