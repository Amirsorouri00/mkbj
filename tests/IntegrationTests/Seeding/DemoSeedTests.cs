using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Host.Seeding;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Seeding;

/// <summary>
/// The demo seed against a real database and a real object store.
/// </summary>
/// <remarks>
/// One test, not four: a fresh database plus 160 published products is the expensive part, and
/// everything asserted here is about the same single run. It needs its own database because the
/// shared one is where every other test's assumptions live.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class DemoSeedTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seed_Creates_400_Sellable_Variants_And_Is_Idempotent()
    {
        using var factory = await fixture.CreateFreshDatabaseFactoryAsync();

        // --seed reaches the demo catalogue through this list and nothing else, so forgetting to
        // register it would leave every assertion below passing on a seed nobody can run.
        using (var registration = factory.Services.CreateScope())
        {
            registration.ServiceProvider.GetServices<IDataSeeder>()
                .Should().ContainSingle(seeder => seeder is DemoCatalogDataSeeder);
        }

        var first = await RunAsync(factory);
        first.Products.Should().Be(160);
        first.Variants.Should().Be(400);

        (await SellableAsync(factory)).Should().Be(400,
            "every seeded product is published active with a usable image");

        // Run it again, because that is the seeder's whole promise: it publishes through the
        // catalogue's own idempotent port, so a second run must find everything already there.
        var second = await RunAsync(factory);
        second.Variants.Should().Be(400,
            "the publishing port matched every product by its GTIN and updated it in place");

        (await SellableAsync(factory)).Should().Be(400, "a second run must not double the shop");

        using var scope = factory.Services.CreateScope();
        var sellable = await scope.ServiceProvider.GetRequiredService<ISellableVariantPort>()
            .GetSellableAsync(null, Ct);

        var quotes = (await scope.ServiceProvider.GetRequiredService<IPriceQuotePort>()
            .QuoteAsync(sellable.Select(variant => variant.VariantId).ToList(), "USD",
                DateTimeOffset.UtcNow, Ct))
            .Value!;

        quotes.Should().HaveCount(400,
            "a variant that cannot be quoted is omitted silently, so a short count here means "
            + "the seed produced something unsellable");

        // The two pinned extremes have to survive to the quote: this is the range composition and
        // the price band are expected to cope with.
        var amounts = quotes.Values.Select(quote => quote.ReferenceMinor).ToList();
        amounts.Min().Should().BeLessThanOrEqualTo(DemoCatalogData.MinPriceMinor);
        amounts.Max().Should().BeGreaterThanOrEqualTo(DemoCatalogData.MaxPriceMinor);
    }

    private static async Task<SeedSummary> RunAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var result = await DemoCatalogSeeder.RunAsync(scope.ServiceProvider, Ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value!;
    }

    private static async Task<int> SellableAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ISellableVariantPort>()
            .GetSellableAsync(null, Ct)).Count;
    }
}
