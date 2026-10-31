using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using EwaysVNext.Database;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.LocalStack;
using Testcontainers.PostgreSql;
using Xunit;
using AuthorizationService.Implementation.Persistence;
using EwaysVNext.Presentation.Endpoint.Security;

namespace EwaysVNext.IntegrationTests;

/// <summary>
/// The real platform the suite runs against: a Postgres container built by the same
/// <c>build/postgres/init/01-roles.sql</c> production uses, and an S3 server with the three
/// buckets. Tests connect to Postgres as <c>eways_app</c>, the role that owns nothing — so
/// anything the schema forbids the application fails here too, instead of passing as a superuser
/// and failing in production.
/// <para>
/// The S3 server is LocalStack, not MinIO: the <c>minio/minio</c> image cannot be pulled in this
/// environment (ADR-0017 D11). The adapter speaks plain S3 and MinIO implements S3, so this
/// exercises the adapter — but it does not exercise MinIO itself, and a MinIO-specific quirk would
/// not be caught here.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string TestScheme = "IntegrationTest";
    private const string AppPassword = "app";
    private const string MigratorPassword = "migrator";
    private const string SuperuserPassword = "postgres";
    private const string Database = "eways";

    // Testcontainers 4.16 marks the parameterless builder constructors obsolete but ships no
    // public replacement. SuppressMessage does not apply to compiler warnings, so this is a pragma.
#pragma warning disable CS0618
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase(Database)
        .WithUsername("postgres")
        .WithPassword(SuperuserPassword)
        .WithEnvironment("APP_DB_PASSWORD", AppPassword)
        .WithEnvironment("MIGRATOR_DB_PASSWORD", MigratorPassword)
        .WithResourceMapping(
            new FileInfo(Path.Combine(RepositoryRoot, "build", "postgres", "init", "01-roles.sql")),
            "/docker-entrypoint-initdb.d/")
        .Build();

#pragma warning disable CS0618
    private readonly LocalStackContainer _s3 = new LocalStackBuilder()
        .WithImage("localstack/localstack:4.0")
        // Off by default, which would make every presigned-URL expiry assertion pass vacuously.
        .WithEnvironment("S3_SKIP_SIGNATURE_VALIDATION", "0")
        .Build();
#pragma warning restore CS0618
#pragma warning restore CS0618

    public const string TestRouterScheme = "IntegrationTestRouter";

    public string SuperuserConnectionString => _postgres.GetConnectionString();
    public string AppConnectionString => ConnectionStringFor("eways_app", AppPassword);
    public string MigratorConnectionString => ConnectionStringFor("eways_migrator", MigratorPassword);

    public string StorageEndpoint => _s3.GetConnectionString();
    public const string StorageAccessKey = "test";
    public const string StorageSecretKey = "test";

    public WebApplicationFactory<Program> CreateFactory(
        IReadOnlyDictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = AppConnectionString,
            ["Minio:Endpoint"] = StorageEndpoint,
            ["Minio:PublicEndpoint"] = StorageEndpoint,
            ["Minio:AccessKey"] = StorageAccessKey,
            ["Minio:SecretKey"] = StorageSecretKey,
            ["Jobs:Mode"] = "Disabled",
            ["ApiKeys:Pepper"] = "integration-tests-pepper-forty-chars-0000",
            // The suite fires many requests from one address; without this the per-IP limiter
            // would reject them and every test would be measuring the limiter.
            ["RateLimiting:AnonymousPermitPerMinute"] = "100000",
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
                configureServices?.Invoke(services);

                // Production routes a partner key away from the JWT reader with a policy scheme;
                // the suite replaces that router with its own so the same split is exercised here
                // rather than every pk_ key arriving at a JWT handler that cannot read it.
                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = TestRouterScheme;
                    options.DefaultAuthenticateScheme = TestRouterScheme;
                    options.DefaultChallengeScheme = TestRouterScheme;
                })
                .AddPolicyScheme(TestRouterScheme, TestRouterScheme, policy =>
                    policy.ForwardDefaultSelector = context =>
                        AuthSchemes.IsPartnerKey(context.Request)
                            ? AuthSchemes.PartnerApiKey
                            : TestScheme)
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
        await Task.WhenAll(_postgres.StartAsync(), _s3.StartAsync());

        await CreateBucketsAsync();

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
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _s3.DisposeAsync().AsTask());
    }

    private async Task CreateBucketsAsync()
    {
        using var client = new AmazonS3Client(
            StorageAccessKey,
            StorageSecretKey,
            new AmazonS3Config { ServiceURL = StorageEndpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });

        foreach (var bucket in EwaysVNext.Storage.StorageBuckets.All)
        {
            await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
        }
    }

    private string ConnectionStringFor(string user, string password, string? database = null)
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = user,
            Password = password,
            Database = database ?? Database,
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Creates and migrates a database of its own, for the few assertions that are about what a
    /// <b>fresh</b> database contains.
    /// <para>
    /// The seeded settings singletons are the case: every other test shares one database, and the
    /// settings admin tests necessarily change the very rows the seed test asserts — so run in the
    /// wrong order, "the migration seeds these values" became "whatever ran last left these
    /// values". Tests that restored what they changed would have hidden that rather than fixed it.
    /// </para>
    /// </summary>
    public async Task<WebApplicationFactory<Program>> CreateFreshDatabaseFactoryAsync()
    {
        var name = $"eways_fresh_{Guid.NewGuid():N}";

        await using (var admin = new Npgsql.NpgsqlConnection(SuperuserConnectionString))
        {
            await admin.OpenAsync();

            await using var create = new Npgsql.NpgsqlCommand(
                $"CREATE DATABASE \"{name}\" OWNER eways_migrator", admin);
            await create.ExecuteNonQueryAsync();
        }

        // ALTER DEFAULT PRIVILEGES is per-database, so 01-roles.sql only granted them in the
        // database it ran in. Production has exactly one database, which is why the script is
        // right as it stands; a second one here needs the same grants applied again, or the app
        // role connects and can read nothing.
        await using (var admin = new Npgsql.NpgsqlConnection(
            ConnectionStringFor("postgres", SuperuserPassword, name)))
        {
            await admin.OpenAsync();

            await using var grant = new Npgsql.NpgsqlCommand(
                $"""
                GRANT CONNECT ON DATABASE "{name}" TO eways_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE eways_migrator GRANT USAGE ON SCHEMAS TO eways_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE eways_migrator GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO eways_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE eways_migrator GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO eways_app;
                """, admin);
            await grant.ExecuteNonQueryAsync();
        }

        var migratorConnection = ConnectionStringFor("eways_migrator", MigratorPassword, name);

        using (var migrator = CreateFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = migratorConnection,
        }))
        {
            using var scope = migrator.Services.CreateScope();

            await DatabaseMigrator.MigrateAsync(scope.ServiceProvider);
            await scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>().Database.MigrateAsync();
        }

        // Handed back on the app role, like every other factory: the suite proves the production
        // grant split, not just the schema.
        return CreateFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ConnectionStringFor("eways_app", AppPassword, name),
        });
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
