using System.Text;
using EwaysVNext.Database;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Xunit;
using AuthorizationService.Implementation.Persistence;

namespace EwaysVNext.IntegrationTests;

/// <summary>
/// The real platform the suite runs against: a Postgres container built by the same
/// <c>build/postgres/init/01-roles.sql</c> production uses, and a MinIO container with the three
/// buckets. Tests connect as <c>eways_app</c>, the role that owns nothing — so anything the schema
/// forbids the application fails here too, instead of passing as a superuser and failing in
/// production.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string TestScheme = "IntegrationTest";
    private const string AppPassword = "app";
    private const string MigratorPassword = "migrator";
    private const string Database = "eways";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase(Database)
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithEnvironment("APP_DB_PASSWORD", AppPassword)
        .WithEnvironment("MIGRATOR_DB_PASSWORD", MigratorPassword)
        .WithResourceMapping(
            new FileInfo(Path.Combine(RepositoryRoot, "build", "postgres", "init", "01-roles.sql")),
            "/docker-entrypoint-initdb.d/")
        .Build();

    public string SuperuserConnectionString => _postgres.GetConnectionString();
    public string AppConnectionString => ConnectionStringFor("eways_app", AppPassword);
    public string MigratorConnectionString => ConnectionStringFor("eways_migrator", MigratorPassword);

    public WebApplicationFactory<Program> CreateFactory(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = AppConnectionString,
            ["Jobs:Mode"] = "Disabled",
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides) settings[key] = value;
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));

            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = TestScheme;
                    options.DefaultAuthenticateScheme = TestScheme;
                    options.DefaultChallengeScheme = TestScheme;
                })
                .AddJwtBearer(TestScheme, opts =>
                {
                    opts.RequireHttpsMetadata = false;
                    opts.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = AdminJwtFactory.Issuer,
                        ValidateAudience = true,
                        ValidAudience = AdminJwtFactory.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AdminJwtFactory.SigningKey)),
                        ClockSkew = TimeSpan.FromSeconds(30),
                    };
                });
            });
        });
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // Migrate as eways_migrator: the owner role. The app role cannot create objects, which is
        // the point — the suite proves the production grant split, not just the schema.
        using var migrator = CreateFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = MigratorConnectionString,
        });
        using var scope = migrator.Services.CreateScope();

        await DatabaseMigrator.MigrateAsync(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>().Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    private string ConnectionStringFor(string user, string password)
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = user,
            Password = password,
            Database = Database,
        };
        return builder.ConnectionString;
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EwaysVNext.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate the EwaysVNext solution directory.");
        }
    }
}

/// <summary>
/// Every integration class joins this collection so the containers start once per run.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresSuite : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
