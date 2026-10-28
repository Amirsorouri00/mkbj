using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using EwaysVNext.Database;
using Microsoft.AspNetCore.TestHost;

namespace EwaysVNext.IntegrationTests;

public sealed class LocalDbFixture : IAsyncLifetime
{
    private const string Instance = @"(localdb)\MSSQLLocalDB";

    private const string TestScheme = "IntegrationTest";

    private readonly string _runId = $"{Guid.NewGuid():N}"[..8];

    public string ConnectionString =>
        $@"Server={Instance};Database=EwaysVNext_Test_{_runId};Trusted_Connection=True;" +
        "MultipleActiveResultSets=true;TrustServerCertificate=True";

    public WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("ConnectionStrings:Default", ConnectionString),
                ]);
            });

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
        using var bootstrap = CreateFactory();
        using var scope = bootstrap.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        // EnsureCreated (not Migrate): builds the schema straight from the CURRENT model, so
        // the suite never depends on a migration having been generated for whatever the
        // model looks like right now. Real deployments still go through
        // Database.MigrateAsync() / `dotnet ef migrations add`.
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var teardown = CreateFactory();
            using var scope = teardown.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
            await db.Database.EnsureDeletedAsync();
        }
        catch
        {
            // A failed drop must never mask a test failure. Leftovers are named
            // EwaysVNext_Test_* and are harmless.
        }
    }
}

/// <summary>
/// Every integration class joins this collection so the database is created once per run
/// and classes do not race each other on the same LocalDB instance.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LocalDbSuite : ICollectionFixture<LocalDbFixture>
{
    public const string Name = "LocalDb";
}
