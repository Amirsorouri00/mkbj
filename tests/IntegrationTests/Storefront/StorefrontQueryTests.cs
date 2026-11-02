using AwesomeAssertions;
using EwaysVNext.IntegrationTests.Support;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Storefront;
using EwaysVNext.Modules.Catalog.QueryApplication.Storefront;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Storefront;

/// <summary>
/// The browse API's read side, over the materialized views.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class StorefrontQueryTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Home_Has_New_Arrivals_Newest_First_And_No_Best_Sellers_Before_Sales()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);
        var home = Value(await Queries(scope).GetHomeAsync("USD", Ct));

        home.Currency.Should().Be("USD");

        home.BestSellers.Should().BeEmpty(
            "popularity is 0 until the sales tables exist, and a section ranked by a column that "
            + "is always zero would be a fabricated one");

        var ours = home.NewArrivals.Select(card => card.ProductId).ToList();
        ours.Should().Contain([scenario.P1, scenario.P2]);
        ours.Should().NotContain(scenario.P3, "a Draft product is not an arrival");

        // Newest first: this scenario's products are the most recent in the shop, and P2 was
        // created after P1.
        ours.IndexOf(scenario.P2).Should().BeLessThan(ours.IndexOf(scenario.P1));
    }

    [Fact]
    public async Task Home_Featured_Categories_Are_At_Most_Eight_With_Products()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);
        var home = Value(await Queries(scope).GetHomeAsync("USD", Ct));

        home.FeaturedCategories.Should().HaveCountLessThanOrEqualTo(8);
        home.FeaturedCategories.Should().OnlyContain(tile => tile.ProductCount > 0,
            "a tile nobody can click through to anything is worse than no tile");

        home.FeaturedCategories.Select(tile => tile.CategoryId)
            .Should().Contain(scenario.RootCategoryId);
    }

    [Fact]
    public async Task Pdp_Returns_Variants_Prices_Media_And_Breadcrumb()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);
        var home = Value(await Queries(scope).GetHomeAsync("USD", Ct));
        var slug = home.NewArrivals.Single(card => card.ProductId == scenario.P1).Slug;

        var product = Value(await Queries(scope).GetProductBySlugAsync(slug, "USD", Ct));

        product.ProductId.Should().Be(scenario.P1);
        product.Variants.Select(offer => offer.Price.Amount).Should().Equal("20.00", "25.00");
        product.Variants.Should().OnlyContain(offer => offer.Price.Currency == "USD");

        product.AttributeOptions.Should().ContainKey("colour")
            .WhoseValue.Should().Equal("Blue", "Red");

        product.Breadcrumb.Select(crumb => crumb.CategoryId)
            .Should().BeEquivalentTo([scenario.RootCategoryId, scenario.ChildCategoryId],
                config => config.WithStrictOrdering(), "a breadcrumb reads root to leaf");

        product.ImageUrls.Should().NotBeEmpty();
        product.ImageUrls.Should().OnlyContain(url => url.StartsWith("/media/products/"));
    }

    [Fact]
    public async Task Pdp_Of_Draft_Is_Not_Found()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();

        var scenario = await StorefrontScenario.CreateAsync(scope.ServiceProvider, Ct);

        // The slug exists in catalog.products; the product is not in the storefront. A visitor
        // must not be able to tell a Draft product from a misspelled slug.
        var slug = await SlugOfAsync(scope, scenario.P3);
        var result = await Queries(scope).GetProductBySlugAsync(slug, "USD", Ct);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("NOT_FOUND");
        result.Error.HttpStatus.Should().Be(404);
    }

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("")]
    public async Task Unknown_Or_Disabled_Currency_Is_Currency_Not_Enabled(string currency)
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();

        var home = await Queries(scope).GetHomeAsync(currency, Ct);

        home.IsFailure.Should().BeTrue();
        home.Error!.Code.Should().Be("CURRENCY_NOT_ENABLED");
        home.Error.HttpStatus.Should().Be(422,
            "the request is well formed and the shop simply does not sell in it");
    }

    /// <summary>
    /// A rate can expire between two refreshes, and the row stays in the view until the next one.
    /// The query carries its own <c>fx_valid_until</c> guard for exactly that window.
    /// </summary>
    /// <remarks>
    /// The wait is real seconds because <c>now()</c> is the database's clock, which no test clock
    /// moves. Two seconds is the smallest window that is not a race.
    /// </remarks>
    [Fact]
    public async Task A_Rate_That_Expired_Since_The_Refresh_Is_Not_Offered()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        var scenario = await StorefrontScenario.CreateAsync(services, Ct);
        var admin = services.GetRequiredService<IPricingAdminService>();
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(2);

        // P2 is priced in EUR, so its USD row is a conversion and carries the rate's expiry.
        Succeeded(await admin.SetFxOverrideAsync("EUR", "USD", 1.1m, expiresAt, scenario.Super, Ct));

        try
        {
            await services.GetRequiredService<IStorefrontViewRefresher>().RefreshAllAsync(Ct);

            var slug = await SlugOfAsync(scope, scenario.P2);
            Value(await Queries(scope).GetProductBySlugAsync(slug, "USD", Ct))
                .Variants.Should().NotBeEmpty("the override is still valid");

            await Task.Delay(expiresAt - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(500), Ct);

            Value(await Queries(scope).GetProductBySlugAsync(slug, "USD", Ct))
                .Variants.Should().BeEmpty(
                    "the view still holds the row, and the query must not offer it");
        }
        finally
        {
            Succeeded(await admin.ClearFxOverrideAsync("EUR", "USD", scenario.Super, Ct));
            await services.GetRequiredService<IStorefrontViewRefresher>().RefreshAllAsync(Ct);
        }
    }

    private static void Succeeded(EwaysVNext.Outcomes.Result result) =>
        result.IsSuccess.Should().BeTrue(result.Error?.Message);

    private static void Succeeded<T>(EwaysVNext.Outcomes.Result<T> result) =>
        result.IsSuccess.Should().BeTrue(result.Error?.Message);

    private static IStorefrontQueries Queries(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IStorefrontQueries>();

    private static async Task<string> SlugOfAsync(IServiceScope scope, long productId)
    {
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNext.Database.EwaysVNextDbContext>();
        var product = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstAsync(db.Products.Where(candidate => candidate.Id == productId), Ct);

        return product.Slug;
    }

    private static T Value<T>(EwaysVNext.Outcomes.Result<T> result)
    {
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value!;
    }
}
