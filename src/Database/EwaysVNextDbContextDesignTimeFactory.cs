using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace EwaysVNext.Database;

public sealed class EwaysVNextDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<EwaysVNextDbContext>
{
    private const string HostUserSecretsId = "1d39ed74-2f0c-4885-9395-409036740084";

    public EwaysVNextDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(HostUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(EwaysVNextDbContextDesignTimeFactoryMessages.ConnectionStringNotConfigured);

        var options = new DbContextOptionsBuilder<EwaysVNextDbContext>()
            .UseSqlServer(
                connectionString,
                sql =>
                {
                    sql.MigrationsAssembly(typeof(EwaysVNextDbContext).Assembly.FullName);
                    sql.MigrationsHistoryTable("__EFMigrationsHistory");
                })
            .Options;

        return new EwaysVNextDbContext(options);
    }
}
