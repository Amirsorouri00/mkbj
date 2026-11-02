using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Commands;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Pricing;

/// <summary>
/// The pricing API end to end, and acceptance 8's pricing half: a base price converts to exact
/// minor units, an override replaces the conversion, and the snapshot a quote carries still
/// points at the rate it used after a newer one arrives.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class PricingRestTests(PostgresFixture fixture)
{
    private const string AdminBase = "/api/v1/admin/pricing";

    private static readonly AdminActor Seeder = new(Guid.NewGuid(), IsSuperAdmin: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static HttpClient Client(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Staff(WebApplicationFactory<Program> factory) =>
        Client(factory, AdminJwtFactory.CreateStaffToken());

    private static HttpClient Super(WebApplicationFactory<Program> factory) =>
        Client(factory, AdminJwtFactory.CreateSuperAdminToken());

    private static async Task<T> InScopeAsync<T>(
        WebApplicationFactory<Program> factory, Func<IServiceProvider, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>A product with one variant, built through the catalogue's own admin service.</summary>
    private static async Task<long> SeedVariantAsync(WebApplicationFactory<Program> factory, string suffix) =>
        await InScopeAsync(factory, async services =>
        {
            var brands = services.GetRequiredService<IBrandAdminService>();
            var categories = services.GetRequiredService<ICategoryService>();
            var catalog = services.GetRequiredService<ICatalogAdminService>();

            var brandId = (await brands.CreateAsync($"Brand {suffix}", [], Seeder, Ct)).Value;
            var categoryId = (await categories.CreateAsync(
                new CreateCategoryCommand($"Kettles {suffix}", $"kettles-{suffix}", null, null, 0), Ct)).Value;

            var productId = (await catalog.CreateProductAsync(
                new CreateProductCommand(brandId, categoryId, $"Kettle {suffix}", null, null, null),
                Seeder, Ct)).Value;

            return (await catalog.AddVariantAsync(
                new AddVariantCommand(productId, $"SKU-{suffix}", new Dictionary<string, string>()),
                Seeder, Ct)).Value;
        });

    private static async Task IngestAsync(
        WebApplicationFactory<Program> factory, DateOnly day, params (string Quote, decimal Rate)[] rates)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IFxRefreshService>().IngestAsync(
            new FxProviderBatch("ecb", day, rates.Select(r => new FxQuote(r.Quote, r.Rate)).ToList()), Ct);
    }

    [Fact]
    public async Task Acceptance08_Base_Conversion_And_Override_Give_Exact_Minor_Units_And_Stable_Snapshot()
    {
        using var factory = fixture.CreateFactory();
        using var staff = Staff(factory);
        var suffix = Suffix();
        var now = DateTimeOffset.UtcNow;

        var variantId = await SeedVariantAsync(factory, suffix);
        await IngestAsync(factory, DateOnly.FromDateTime(now.UtcDateTime), ("USD", 1.0812m), ("SGD", 1.4321m));

        using var priced = await staff.PutAsJsonAsync(
            $"{AdminBase}/variants/{variantId}/price", new { amount = "100.00", currency = "EUR" });
        priced.StatusCode.Should().Be(HttpStatusCode.OK);

        using var overridden = await staff.PutAsJsonAsync(
            $"{AdminBase}/variants/{variantId}/overrides/SGD", new { amount = "150.00" });
        overridden.StatusCode.Should().Be(HttpStatusCode.OK);

        var usd = await InScopeAsync(factory, async services =>
            (await services.GetRequiredService<IPriceQuotePort>()
                .QuoteAsync([variantId], "USD", now, Ct)).Value![variantId]);

        usd.ReferenceMinor.Should().Be(10812);
        usd.Source.Should().Be(PriceSource.Fx);

        var sgd = await InScopeAsync(factory, async services =>
            (await services.GetRequiredService<IPriceQuotePort>()
                .QuoteAsync([variantId], "SGD", now, Ct)).Value![variantId]);

        sgd.ReferenceMinor.Should().Be(15000);
        sgd.Source.Should().Be(PriceSource.Override,
            "an admin's price in a currency is not recomputed when the rate moves");

        // A newer rate arrives for the next day.
        var tomorrow = now.AddDays(1);
        await IngestAsync(factory, DateOnly.FromDateTime(tomorrow.UtcDateTime), ("USD", 1.0900m));

        var later = await InScopeAsync(factory, async services =>
            (await services.GetRequiredService<IPriceQuotePort>()
                .QuoteAsync([variantId], "USD", tomorrow, Ct)).Value![variantId]);

        later.ReferenceMinor.Should().Be(10900);

        // The earlier quote's snapshot still points at an unchanged row: the table is
        // append-only, so what that price was quoted against stays knowable.
        var original = await InScopeAsync(factory, services =>
            services.GetRequiredService<IFxRateRepository>().GetByIdAsync(usd.Fx!.FxRateId!.Value, Ct));

        original!.Rate.Should().Be(1.0812m);
    }

    [Fact]
    public async Task Acceptance13_Staff_Gets_403_On_Fx_Override_And_Currency_Enablement()
    {
        using var factory = fixture.CreateFactory();
        using var staff = Staff(factory);
        using var super = Super(factory);

        using var staffOverride = await staff.PostAsJsonAsync(
            $"{AdminBase}/fx/overrides", new { @base = "EUR", quote = "USD", rate = "1.2000000000" });
        staffOverride.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var staffCurrency = await staff.PutAsJsonAsync(
            $"{AdminBase}/currencies/SGD", new { enabled = true });
        staffCurrency.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var superOverride = await super.PostAsJsonAsync(
            $"{AdminBase}/fx/overrides", new { @base = "EUR", quote = "USD", rate = "1.2000000000" });
        superOverride.StatusCode.Should().Be(HttpStatusCode.Created);

        using var superCurrency = await super.PutAsJsonAsync(
            $"{AdminBase}/currencies/SGD", new { enabled = true });
        superCurrency.StatusCode.Should().Be(HttpStatusCode.OK);

        // Leave the shop as it was found: this fixture's database is shared.
        using var cleared = await super.DeleteAsync($"{AdminBase}/fx/overrides/EUR/USD");
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Shop_Currencies_Is_Anonymous_And_Lists_Five()
    {
        using var factory = fixture.CreateFactory();
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.GetAsync("/api/v1/shop/currencies");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a storefront asks this before a visitor has done anything");

        var codes = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray()
            .Select(currency => currency.GetProperty("code").GetString()!.Trim())
            .ToList();

        codes.Should().Contain(["USD", "EUR", "SGD", "AED", "GBP"]);
    }

    [Fact]
    public async Task Disabled_Currency_Disappears_From_Shop_Currencies()
    {
        using var factory = fixture.CreateFactory();
        using var super = Super(factory);
        using var anonymous = factory.CreateClient();

        using var disabled = await super.PutAsJsonAsync(
            $"{AdminBase}/currencies/AED", new { enabled = false });
        disabled.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            using var response = await anonymous.GetAsync("/api/v1/shop/currencies");
            var codes = (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("data").EnumerateArray()
                .Select(currency => currency.GetProperty("code").GetString()!.Trim())
                .ToList();

            codes.Should().NotContain("AED");
        }
        finally
        {
            using var restored = await super.PutAsJsonAsync(
                $"{AdminBase}/currencies/AED", new { enabled = true });
            restored.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData("100")]
    [InlineData("100.0")]
    [InlineData("100.000")]
    [InlineData("-100.00")]
    public async Task Price_Amount_Must_Be_Two_Decimal_String(string amount)
    {
        using var factory = fixture.CreateFactory();
        using var staff = Staff(factory);
        var variantId = await SeedVariantAsync(factory, Suffix());

        using var response = await staff.PutAsJsonAsync(
            $"{AdminBase}/variants/{variantId}/price", new { amount, currency = "EUR" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("1.00000000001")]
    [InlineData("0")]
    [InlineData("-1.0")]
    [InlineData("1e3")]
    public async Task An_Fx_Override_Rate_Must_Be_A_Plain_Decimal_Within_Ten_Places(string rate)
    {
        using var factory = fixture.CreateFactory();
        using var super = Super(factory);

        using var response = await super.PostAsJsonAsync(
            $"{AdminBase}/fx/overrides", new { @base = "EUR", quote = "GBP", rate });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the column is numeric(18,10), so anything finer is precision the database drops");
    }

    [Fact]
    public async Task A_Variant_With_No_Price_Reads_As_Price_Missing()
    {
        using var factory = fixture.CreateFactory();
        using var staff = Staff(factory);
        var variantId = await SeedVariantAsync(factory, Suffix());

        using var response = await staff.GetAsync($"{AdminBase}/variants/{variantId}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("PRICE_MISSING");
    }

    [Fact]
    public async Task A_Customer_Token_Cannot_Reach_The_Pricing_Admin_Surface()
    {
        using var factory = fixture.CreateFactory();
        using var customer = Client(factory, AdminJwtFactory.CreateCustomerToken());

        using var response = await customer.GetAsync($"{AdminBase}/currencies");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
