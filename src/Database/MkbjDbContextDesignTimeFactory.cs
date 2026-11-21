using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Mkbj.Database;

public sealed class MkbjDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<MkbjDbContext>
{
    private const string HostUserSecretsId = "1d39ed74-2f0c-4885-9395-409036740084";

    public MkbjDbContext CreateDbContext(string[] args)
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
            : "Host=localhost;Database=mkbj_design;Username=design;Password=design";

        var options = new DbContextOptionsBuilder<MkbjDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(MkbjDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public");
                })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new MkbjDbContext(options);
    }
}
