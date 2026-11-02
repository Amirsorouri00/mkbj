using System.Buffers.Binary;
using AwesomeAssertions;
using EwaysVNext.Core.Application.FileTypeDetection;
using EwaysVNext.Core.Implementation.FileTypeDetection;
using EwaysVNext.Host.Seeding;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Seeding;

/// <summary>
/// The demo catalogue's guard rails. The data is fictitious on purpose and the guard exists so
/// it stays that way — out of production, and recognisable as demo data wherever it turns up.
/// </summary>
public sealed class DemoSeedGuardTests
{
    private static IHostEnvironment Environment(string name)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    [Fact]
    public void Seed_Is_Refused_In_Production() =>
        DemoSeedGuard.EnsureAllowed(Environment(Environments.Production))
            .Error!.Code.Should().Be("SEED_REFUSED_IN_PRODUCTION");

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void Seed_Is_Allowed_Everywhere_Else(string name) =>
        DemoSeedGuard.EnsureAllowed(Environment(name)).IsSuccess.Should().BeTrue();
}

/// <summary>
/// The hand-written PNG encoder. A malformed chunk produces a file no viewer opens, and the
/// mistakes are all silent — so the structure is asserted byte by byte.
/// </summary>
public sealed class PlaceholderPngTests
{
    [Fact]
    public void Png_Has_Signature_And_Dimensions()
    {
        var png = PlaceholderPng.Create(600, 400, 10, 20, 30);

        png[..8].Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)).Should().Be(600);
        BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)).Should().Be(400);
    }

    [Fact]
    public void The_Catalogue_Accepts_It_As_A_Png() =>
        new FileTypeDetector().Detect(PlaceholderPng.Create(600, 400, 10, 20, 30))
            .FileType.Should().Be(FileType.Png,
                "the seed uploads these through the same type sniff an admin upload goes through");

    [Fact]
    public void It_Ends_With_An_IEND_Chunk()
    {
        var png = PlaceholderPng.Create(16, 16, 1, 2, 3);

        System.Text.Encoding.ASCII.GetString(png.AsSpan(png.Length - 8, 4))
            .Should().Be("IEND", "a PNG without a terminator is a truncated file");
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    [InlineData(-1, 16)]
    public void A_Zero_Or_Negative_Dimension_Is_Refused(int width, int height) =>
        FluentActions.Invoking(() => PlaceholderPng.Create(width, height, 1, 2, 3))
            .Should().Throw<ArgumentOutOfRangeException>();
}

/// <summary>
/// The generated catalogue. Deterministic by design: a demo that differs per run turns a
/// reproducible bug into a story about what somebody saw once.
/// </summary>
public sealed class DemoCatalogDataTests
{
    [Fact]
    public void Demo_Data_Has_400_Variants_In_12_Categories_Spanning_50_To_5000000_Minor()
    {
        DemoCatalogData.Categories.Should().HaveCount(12);
        DemoCatalogData.Products.SelectMany(product => product.Variants).Should().HaveCount(400);

        var prices = DemoCatalogData.Products
            .SelectMany(product => product.Variants)
            .Select(variant => variant.BaseAmountMinor)
            .ToList();

        prices.Min().Should().Be(50);
        prices.Max().Should().Be(5_000_000);

        DemoCatalogData.Products.Should().OnlyContain(
            product => product.Title.StartsWith("[DEMO] ", StringComparison.Ordinal));
        DemoCatalogData.Products.Select(product => product.Gtin)
            .Should().OnlyHaveUniqueItems()
            .And.OnlyContain(gtin => gtin.StartsWith("20", StringComparison.Ordinal));
    }

    [Fact]
    public void The_Data_Is_The_Same_On_Every_Read()
    {
        var first = DemoCatalogData.Products.Select(product => product.Title).ToList();
        var second = DemoCatalogData.Products.Select(product => product.Title).ToList();

        second.Should().Equal(first,
            "a demo that differs per run cannot be used to reproduce anything");
    }

    [Fact]
    public void Every_Gtin_Passes_The_Catalogue_Own_Validation()
    {
        // The check digits are real, so publishing these exercises the identifier rules rather
        // than skirting them.
        foreach (var gtin in DemoCatalogData.Products.Select(product => product.Gtin))
        {
            FluentActions.Invoking(() =>
                    EwaysVNext.Modules.Catalog.Application.Products.Domain.ProductIdentifier.Normalize(
                        EwaysVNext.Modules.Catalog.Application.Products.Domain.ProductIdentifierType.Gtin,
                        gtin, null))
                .Should().NotThrow(gtin);
        }
    }

    [Fact]
    public void Single_Variant_Products_Carry_An_Empty_Signature() =>
        DemoCatalogData.Products
            .Where(product => product.Variants.Count == 1)
            .Should().NotBeEmpty()
            .And.OnlyContain(product => product.Variants[0].Attributes.Count == 0,
                "a lone variant means 'this product has only one form', which is the shape most "
                + "real products take");

    [Fact]
    public void Multi_Variant_Products_Use_One_Axis()
    {
        var multi = DemoCatalogData.Products.Where(product => product.Variants.Count > 1).ToList();

        multi.Should().NotBeEmpty();
        multi.Should().OnlyContain(product =>
            product.Variants.SelectMany(variant => variant.Attributes.Keys).Distinct().Count() == 1);
    }

    [Fact]
    public void Prices_Are_Spread_Across_The_Range_Rather_Than_Bunched_At_The_Top()
    {
        var prices = DemoCatalogData.Products
            .SelectMany(product => product.Variants)
            .Select(variant => variant.BaseAmountMinor)
            .ToList();

        // A uniform draw over four orders of magnitude leaves almost nothing below $100.
        // Log-uniform is what keeps the cheap end — where rounding and the band are most
        // fragile — actually represented.
        prices.Count(price => price < 10_000).Should().BeGreaterThan(prices.Count / 5);
    }

    [Fact]
    public void Base_Currencies_Are_Mixed()
    {
        var currencies = DemoCatalogData.Products
            .SelectMany(product => product.Variants)
            .Select(variant => variant.BaseCurrency)
            .Distinct()
            .ToList();

        currencies.Should().Contain(["USD", "EUR", "SGD", "GBP"],
            "a demo priced entirely in one currency would exercise no conversion at all");
    }
}
