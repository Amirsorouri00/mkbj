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

        // `dotnet ef migrations add` builds the relational model without ever connecting, so a
        // placeholder is correct here: requiring a real connection string would make generating a
        // migration depend on having a database.
        var connectionString = configuration.GetConnectionString("Default") is { Length: > 0 } configured
            ? configured
            : "Host=localhost;Database=eways_design;Username=design;Password=design";

        var options = new DbContextOptionsBuilder<EwaysVNextDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(EwaysVNextDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public");
                })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new EwaysVNextDbContext(options);
    }
}
