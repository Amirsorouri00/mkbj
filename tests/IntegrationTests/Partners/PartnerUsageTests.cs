using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Database;
using EwaysVNext.Domain.Common;
using EwaysVNext.IntegrationTests.Support;
using EwaysVNext.Jobs;
using EwaysVNext.Modules.Partners.Adapter.Outbound.Usage;
using EwaysVNext.Modules.Partners.Application.Usage;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Xunit;

namespace EwaysVNext.IntegrationTests.Partners;

[Collection(PostgresSuite.Name)]
public sealed class PartnerUsageTests(PostgresFixture fixture)
{
    private static readonly string OrdersCreate =
        $"/test/partner/{PartnerProbeEndpoints.Slug(PartnerScopes.OrdersCreate)}";

    private static readonly string CustomersRead =
        $"/test/partner/{PartnerProbeEndpoints.Slug(PartnerScopes.CustomersRead)}";

    private WebApplicationFactory<Program> CreateFactory(
        Action<IServiceCollection>? configure = null,
        IReadOnlyDictionary<string, string?>? overrides = null) =>
        fixture.CreateFactory(overrides, services =>
        {
            services.AddScoped<IEndpoint, PartnerProbeEndpoints>();
            configure?.Invoke(services);
        });

    /// <summary>
    /// The flush is asynchronous by design, so the test waits for the row rather than assuming
    /// it is already there — and fails on the timeout rather than hanging.
    /// </summary>
    private static async Task<List<ApiKeyUsage>> WaitForRows(
        WebApplicationFactory<Program> factory, long partnerId, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            using var scope = factory.Services.CreateScope();
            var rows = await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
                .ApiKeyUsages.AsNoTracking()
                .Where(usage => usage.PartnerId == partnerId)
                .ToListAsync();

            if (rows.Count >= expected)
                return rows;

            await Task.Delay(100);
        }

        return [];
    }

    [Fact]
    public async Task Partner_Requests_Are_Recorded_In_The_Usage_Log()
    {
        using var factory = CreateFactory();
        var (partnerId, keyId, key) = await PartnerSeeder.CreateAsync(factory, [PartnerScopes.OrdersCreate]);

        using var client = factory.CreateClient();
        await client.GetWithBearerAsync(OrdersCreate, key);    // 200
        await client.GetWithBearerAsync(CustomersRead, key);   // 403: scope not granted

        var rows = await WaitForRows(factory, partnerId, expected: 2);

        rows.Should().HaveCount(2);
        rows.Select(row => row.Status).Should().BeEquivalentTo([200, 403],
            "a refusal is exactly what a partner asks about later");
        rows.Should().OnlyContain(row => row.KeyId == keyId);
        rows.Should().OnlyContain(row => row.Method == "GET");
        rows.Should().OnlyContain(row => row.Path.StartsWith("/test/partner/", StringComparison.Ordinal));
        rows.Should().OnlyContain(row => row.CorrelationId.Length > 0);
    }

    [Fact]
    public async Task An_Unauthenticated_Request_Is_Not_Recorded()
    {
        using var factory = CreateFactory();
        var (partnerId, _, key) = await PartnerSeeder.CreateAsync(factory, [PartnerScopes.OrdersCreate]);

        using var client = factory.CreateClient();
        await client.GetWithBearerAsync(OrdersCreate, "pk_garbage");
        await client.GetWithBearerAsync(OrdersCreate, key);

        var rows = await WaitForRows(factory, partnerId, expected: 1);

        rows.Should().HaveCount(1,
            "there is no partner to attribute a rejected credential to, and attributing it to the "
            + "partner whose key was nearly guessed would be worse than not logging it");
    }

    [Fact]
    public async Task UsagePrune_Deletes_Rows_Older_Than_Retention()
    {
        var clock = new MutableClock();
        using var factory = CreateFactory(services =>
            services.AddSingleton<IClock>(clock));

        var partnerId = 90_001L;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
            db.ApiKeyUsages.AddRange(
                Row(partnerId, 1, clock.UtcNow.AddDays(-91)),
                Row(partnerId, 2, clock.UtcNow.AddDays(-89)));
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            // Resolved by its concrete type, which is how AddJobHandler registers it and how the
            // worker's registry resolves it. Asking for IJobHandler<T> would pass here only if
            // the test registered something production does not.
            var handler = scope.ServiceProvider.GetRequiredService<UsagePruneJobHandler>();
            var outcome = await handler.HandleAsync(
                new UsagePrunePayload(),
                new JobContext(1, 1, _ => Task.CompletedTask));

            outcome.Should().Be(JobOutcome.Completed);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var remaining = await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
                .ApiKeyUsages.AsNoTracking()
                .Where(usage => usage.PartnerId == partnerId)
                .ToListAsync();

            remaining.Should().ContainSingle()
                .Which.Id.Should().Be(2, "only the row inside the 90-day window survives");
        }
    }

    [Fact]
    public async Task Api_Key_Never_Appears_In_Logs()
    {
        var sink = new CapturingSink();

        using var factory = CreateFactory(
            services => services.AddSingleton<ILogEventSink>(sink),
            new Dictionary<string, string?>
            {
                ["Serilog:MinimumLevel:Default"] = "Verbose",
                ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
            });

        var (partnerId, keyId, key) = await PartnerSeeder.CreateAsync(factory, [PartnerScopes.OrdersCreate]);
        var secret = key.Split('_')[2];

        using var client = factory.CreateClient();

        await client.GetWithBearerAsync(OrdersCreate, key);                         // 200
        await client.GetWithBearerAsync(CustomersRead, key);                        // 403
        await client.GetWithBearerAsync(OrdersCreate, Tamper(key));                 // 401, bad secret
        await PartnerSeeder.RevokeAsync(factory, partnerId, keyId);
        await client.GetWithBearerAsync(OrdersCreate, key);                         // 401, revoked

        sink.Events.Should().NotBeEmpty("a sink that captured nothing proves nothing");

        var text = sink.AllText();
        text.Should().NotContain(secret).And.NotContain(key,
            "a log line holding a live credential outlives the request that produced it, and is "
            + "read by more people than the key ever should be");
    }

    /// <summary>Changes the last character, so the prefix still resolves but the secret cannot.</summary>
    private static string Tamper(string key) =>
        key[..^1] + (key[^1] == 'A' ? 'B' : 'A');

    private static ApiKeyUsage Row(long partnerId, long id, DateTimeOffset at) => new()
    {
        Id = id,
        PartnerId = partnerId,
        KeyId = 1,
        At = at,
        Method = "GET",
        Path = "/test",
        Status = 200,
        CorrelationId = "seed",
        DurationMs = 1,
    };
}
