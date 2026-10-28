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

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(AuthorizationDbContextDesignTimeFactoryMessages.ConnectionStringNotConfigured);

        var options = new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseSqlServer(
                connectionString,
                sql =>
                {
                    sql.MigrationsAssembly(typeof(AuthorizationDbContext).Assembly.FullName);
                    sql.MigrationsHistoryTable("__AuthorizationMigrationsHistory", "authorization");
                })
            .Options;

        return new AuthorizationDbContext(options);
    }
}
