using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.IntegrationTests.Support;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Storefront;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Storefront;

/// <summary>
/// The storefront view and <see cref="IPriceQuotePort"/> must agree, to the minor unit.
/// </summary>
/// <remarks>
/// They are two implementations of one rule — one in SQL, one in C# — so nothing but a test keeps
/// them together. A listing that advertises a price the cart then changes is the failure this
/// guards against, and a cent is enough to be noticed.
/// <para>
/// A failure here is a bug in the view SQL or in the resolved-rate projection, never in the test.
/// </para>
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class StorefrontPriceParityTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task View_Prices_Equal_Live_Quotes_For_Every_Currency()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        var scenario = await StorefrontScenario.CreateAsync(services, Ct);
        long[] variants = [scenario.P1Red, scenario.P1Blue, scenario.P2Variant];

        await AssertParityAsync(services, variants);

        // An override on one variant in one currency: the view must switch that row to Override
        // and leave every other row converting.
        Succeeded(await services.GetRequiredService<IPricingAdminService>()
            .SetCurrencyOverrideAsync(scenario.P1Red, "SGD", 9999, scenario.Staff, Ct));

        await services.GetRequiredService<IStorefrontViewRefresher>().RefreshAllAsync(Ct);

        await AssertParityAsync(services, variants);
    }

    [Theory]
    // 137.5 rounds to 138 — up, to the even hundred — and 162.5 rounds to 162, down, for the same
    // reason. Postgres' own round() is half-away-from-zero and would give 163 for the second.
    [InlineData("1.1000000000", 138L)]
    [InlineData("1.3000000000", 162L)]
    public async Task Half_Even_Rounding_Agrees_Between_View_And_Quote(string rate, long expected)
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        var scenario = await StorefrontScenario.CreateAsync(services, Ct);
        var admin = services.GetRequiredService<IPricingAdminService>();

        // 1.25 EUR is 125 minor units, so the conversion lands exactly on a half.
        Succeeded(await admin.SetVariantPriceAsync(scenario.P2Variant, 125, "EUR", scenario.Staff, Ct));
        Succeeded(await admin.SetFxOverrideAsync(
            "EUR", "USD", decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture),
            null, scenario.Super, Ct));

        try
        {
            await services.GetRequiredService<IStorefrontViewRefresher>().RefreshAllAsync(Ct);

            var quoted = (await services.GetRequiredService<IPriceQuotePort>()
                .QuoteAsync([scenario.P2Variant], "USD", DateTimeOffset.UtcNow, Ct))
                .Value![scenario.P2Variant];

            quoted.ReferenceMinor.Should().Be(expected);
            (await ViewPricesAsync(scenario.P2Variant, "USD")).Should().Equal(expected);
        }
        finally
        {
            // The fixture's database is shared, and an FX override moves every EUR price in it.
            Succeeded(await admin.ClearFxOverrideAsync("EUR", "USD", scenario.Super, Ct));
        }
    }

    [Fact]
    public async Task Stale_Rate_Drops_Converted_Prices_After_Reprojection()
    {
        var clock = new MutableClock();
        using var factory = fixture.CreateFactory(
            configureServices: services => services.AddSingleton<IClock>(clock));
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        var scenario = await StorefrontScenario.CreateAsync(services, Ct);

        // Past Fx:MaxRateAge, so every rate ingested by the scenario is too old to price with.
        clock.Advance(TimeSpan.FromHours(73));
        await services.GetRequiredService<IPricingChangeNotifier>().FxChangedAsync(Ct);
        await services.GetRequiredService<IStorefrontViewRefresher>().RefreshAllAsync(Ct);

        (await ViewCurrenciesAsync(scenario.P2Variant)).Should().BeEquivalentTo(["EUR"],
            "a base-currency row needs no rate; every conversion out of EUR has gone stale");

        (await ViewCurrenciesAsync(scenario.P1Red)).Should().BeEquivalentTo(["USD", "AED"],
            "AED is pegged to USD, and a peg does not age");
    }

    [Fact]
    public async Task Disabled_Currency_Leaves_The_Views()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        var scenario = await StorefrontScenario.CreateAsync(services, Ct);
        var admin = services.GetRequiredService<IPricingAdminService>();

        Succeeded(await admin.SetCurrencyEnabledAsync("GBP", enabled: false, scenario.Super, Ct));

        try
        {
            await services.GetRequiredService<IStorefrontViewRefresher>().RefreshAllAsync(Ct);

            (await ViewCurrenciesAsync(scenario.P1Red)).Should().NotContain("GBP",
                "a currency the seller stopped selling in must not stay on the shelf");
        }
        finally
        {
            Succeeded(await admin.SetCurrencyEnabledAsync("GBP", enabled: true, scenario.Super, Ct));
            await services.GetRequiredService<IStorefrontViewRefresher>().RefreshAllAsync(Ct);
        }
    }

    /// <summary>
    /// Every view row for these variants, in every currency the view holds, against the quote the
    /// cart would get for the same variant and currency.
    /// </summary>
    private async Task AssertParityAsync(IServiceProvider services, IReadOnlyList<long> variants)
    {
        var quotes = services.GetRequiredService<IPriceQuotePort>();
        var rows = await ViewRowsAsync(variants);

        rows.Should().NotBeEmpty("a parity test over no rows proves nothing");

        foreach (var currency in rows.Select(row => row.Currency).Distinct())
        {
            var quoted = (await quotes.QuoteAsync(variants, currency, DateTimeOffset.UtcNow, Ct)).Value!;

            foreach (var row in rows.Where(row => row.Currency == currency))
            {
                quoted.Should().ContainKey(row.VariantId,
                    "the view shows {0} in {1}, so the cart must be able to price it",
                    row.VariantId, currency);

                var quote = quoted[row.VariantId];
                quote.ReferenceMinor.Should().Be(row.PriceMinor,
                    "variant {0} in {1}", row.VariantId, currency);
                quote.Source.ToString().Should().Be(row.PriceSource,
                    "variant {0} in {1}", row.VariantId, currency);
            }
        }
    }

    private async Task<List<(long VariantId, string Currency, long PriceMinor, string PriceSource)>>
        ViewRowsAsync(IReadOnlyList<long> variantIds)
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync(Ct);

        await using var command = new NpgsqlCommand(
            """
            SELECT variant_id, currency, price_minor, price_source
            FROM catalog.mv_storefront_variant WHERE variant_id = ANY (@ids)
            """,
            connection);
        command.Parameters.AddWithValue("ids", variantIds.ToArray());

        var rows = new List<(long, string, long, string)>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1).Trim(), reader.GetInt64(2),
                reader.GetString(3)));
        }

        return rows;
    }

    private async Task<List<string>> ViewCurrenciesAsync(long variantId) =>
        [.. (await ViewRowsAsync([variantId])).Select(row => row.Currency).Order()];

    private async Task<List<long>> ViewPricesAsync(long variantId, string currency) =>
        [.. (await ViewRowsAsync([variantId]))
            .Where(row => row.Currency == currency).Select(row => row.PriceMinor)];

    private static void Succeeded(EwaysVNext.Outcomes.Result result) =>
        result.IsSuccess.Should().BeTrue(result.Error?.Message);

    private static void Succeeded<T>(EwaysVNext.Outcomes.Result<T> result) =>
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
}
