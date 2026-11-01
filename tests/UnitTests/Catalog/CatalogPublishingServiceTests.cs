using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products.Services;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// Publishing is what an import does on every pass over the same feed, so the thing being pinned
/// here is that doing it twice changes nothing — and that it never overrules an admin.
/// </summary>
public sealed class CatalogPublishingServiceTests
{
    private const long BrandId = 10;
    private const long CategoryId = 20;
    private const string Gtin = "4006381333931";
    private const string OtherGtin = "0036000291452";

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IBrandAdminService _brands = Substitute.For<IBrandAdminService>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly ICatalogMediaStore _media = Substitute.For<ICatalogMediaStore>();
    private readonly ICatalogChangeNotifier _notifier = Substitute.For<ICatalogChangeNotifier>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FixedClock _clock = new();

    private long _nextId = 1_000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CatalogPublishingServiceTests()
    {
        _ids.Generate().Returns(_ => _nextId++);
        _brands.EnsureAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(BrandId));
        _categories.FindByCanonicalPathAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Category.Create(CategoryId, "Kettles", "kettles", null, null, 0, _clock.UtcNow));
    }

    private CatalogPublishingService CreateService() => new(
        _products, _brands, _categories, _media, _notifier, _unitOfWork, _ids, _clock);

    private static PublishProductCommand Command(
        string title = "Demo Kettle",
        string? gtin = Gtin,
        string? mpn = null,
        long? targetProductId = null,
        IReadOnlyList<PublishMedia>? media = null)
    {
        var identifiers = new List<IdentifierInput>();
        if (gtin is not null) identifiers.Add(new IdentifierInput(ProductIdentifierType.Gtin, gtin, null));
        if (mpn is not null) identifiers.Add(new IdentifierInput(ProductIdentifierType.Mpn, mpn, null));

        return new PublishProductCommand(
            new PublishBrand("Northwind", []),
            ["kitchen", "kettles"],
            title,
            null,
            null,
            identifiers,
            media ?? [new PublishMedia("staging/kettle.png", "kettle", "https://example/kettle.png",
                MediaUsageRights.Allowed, 0)],
            [new PublishVariant(new Dictionary<string, string> { ["Size"] = "1.7L" }, "SKU-PUB1")],
            [77],
            "import");
    }

    /// <summary>An active product carrying the given GTIN, one variant and one usable image.</summary>
    private Product ActiveProductWithGtin(string gtin, long id = 500)
    {
        var product = Product.CreateDraft(
            id, BrandId, CategoryId, "Demo Kettle", "demo-kettle", null, null, _clock.UtcNow);
        product.AddIdentifier(_nextId++, ProductIdentifierType.Gtin, gtin, null, _clock.UtcNow);
        product.AddVariant(_nextId++, "SKU-EXIST", new Dictionary<string, string> { ["Size"] = "1.7L" }, _clock.UtcNow);
        product.AddMedia(_nextId++, $"products/{id}/1.png", 0, "kettle", "https://example/kettle.png",
            MediaUsageRights.Allowed, _clock.UtcNow);
        product.Activate(byAdmin: true, _clock.UtcNow);
        return product;
    }

    private void MatchesGtin(string gtin, Product product) =>
        _products.FindByIdentifierAsync(ProductIdentifierType.Gtin, gtin, null, Arg.Any<CancellationToken>())
            .Returns(product);

    [Fact]
    public async Task Publish_Updates_But_Never_Reactivates_Admin_Deactivated_Product()
    {
        var existing = ActiveProductWithGtin(Gtin);
        existing.Deactivate(byAdmin: true, _clock.UtcNow);
        MatchesGtin(Gtin, existing);

        var result = await CreateService().UpsertProductAsync(Command(title: "Renamed Kettle"), Ct);

        result.Value!.SkippedBecauseDeactivated.Should().BeTrue();
        result.Value.Created.Should().BeFalse();
        existing.Title.Should().Be("Renamed Kettle", "the import's work is still done");
        existing.Status.Should().Be(ProductStatus.Deactivated);
        existing.DeactivatedByAdmin.Should().BeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gtin_Match_Wins_Over_Brand_Mpn()
    {
        var byGtin = ActiveProductWithGtin(Gtin, 500);
        var byMpn = ActiveProductWithGtin(OtherGtin, 600);
        MatchesGtin(Gtin, byGtin);
        _products.FindByBrandAndMpnAsync(BrandId, "MX-900", Arg.Any<CancellationToken>()).Returns(byMpn);

        var result = await CreateService().UpsertProductAsync(
            Command(title: "Renamed", mpn: "MX-900"), Ct);

        result.Value!.ProductId.Should().Be(500,
            "a GS1 code names the article worldwide; a part number only names it within a brand");
        byMpn.Title.Should().Be("Demo Kettle", "the weaker match was never touched");
    }

    [Fact]
    public async Task Match_Follows_MergedInto_To_Survivor()
    {
        var duplicate = ActiveProductWithGtin(Gtin, 500);
        var survivor = ActiveProductWithGtin(OtherGtin, 600);
        duplicate.MarkMergedInto(600, _clock.UtcNow);
        MatchesGtin(Gtin, duplicate);
        _products.GetByIdAsync(600, Arg.Any<CancellationToken>()).Returns(survivor);

        var result = await CreateService().UpsertProductAsync(Command(title: "Renamed"), Ct);

        result.Value!.ProductId.Should().Be(600,
            "a feed still carrying a duplicate's code must update the product customers can buy");
        survivor.Title.Should().Be("Renamed");
    }

    [Fact]
    public async Task A_Merge_Chain_That_Does_Not_End_Is_Refused()
    {
        // Two products pointing at each other — something a direct database write could leave
        // behind, and which the walk must refuse rather than follow forever.
        var first = ActiveProductWithGtin(Gtin, 500);
        var second = ActiveProductWithGtin(OtherGtin, 600);
        first.MarkMergedInto(600, _clock.UtcNow);
        second.MarkMergedInto(500, _clock.UtcNow);
        MatchesGtin(Gtin, first);
        _products.GetByIdAsync(500, Arg.Any<CancellationToken>()).Returns(first);
        _products.GetByIdAsync(600, Arg.Any<CancellationToken>()).Returns(second);

        var result = await CreateService().UpsertProductAsync(Command(), Ct);

        result.Error!.Code.Should().Be("PRODUCT_MERGE_CYCLE");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TargetProductId_Bypasses_Identifier_Matching()
    {
        var target = ActiveProductWithGtin(OtherGtin, 5);
        _products.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(target);
        // A GTIN that matches nothing; the admin has already said which product this is.
        _products.FindByIdentifierAsync(
            ProductIdentifierType.Gtin, Gtin, null, Arg.Any<CancellationToken>()).Returns((Product?)null);

        var command = Command(title: "Admin Says This One") with { TargetProductId = 5 };
        var result = await CreateService().UpsertProductAsync(command, Ct);

        result.Value!.ProductId.Should().Be(5);
        result.Value.Created.Should().BeFalse();
        target.Title.Should().Be("Admin Says This One");
        await _products.DidNotReceive().AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Republishing_Same_Command_Creates_Nothing_New()
    {
        var existing = ActiveProductWithGtin(Gtin);
        MatchesGtin(Gtin, existing);
        var mediaCountBefore = existing.Media.Count;
        var identifierCountBefore = existing.Identifiers.Count;

        var result = await CreateService().UpsertProductAsync(Command(), Ct);

        result.Value!.Created.Should().BeFalse();
        result.Value.Variants.Should().OnlyContain(variant => !variant.Created,
            "the variant's attribute signature is the same, so it is the same variant");
        await _products.DidNotReceive().AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());

        existing.Media.Count.Should().Be(mediaCountBefore,
            "the image came from the same source URL, so it is the same image");
        existing.Identifiers.Count.Should().Be(identifierCountBefore);
        await _media.DidNotReceiveWithAnyArgs().CopyFromStagingAsync(default!, default!, default);
    }

    [Fact]
    public async Task A_New_Product_Is_Created_Activated_And_Announced()
    {
        _products.FindByIdentifierAsync(
            Arg.Any<ProductIdentifierType>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns((Product?)null);

        var result = await CreateService().UpsertProductAsync(Command(), Ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value!.Created.Should().BeTrue();
        result.Value.Variants.Should().OnlyContain(variant => variant.Created);

        await _products.Received(1).AddAsync(
            Arg.Is<Product>(product => product.Status == ProductStatus.Active), Arg.Any<CancellationToken>());
        await _media.Received(1).CopyFromStagingAsync(
            "staging/kettle.png", Arg.Is<string>(key => key.EndsWith(".png", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await _notifier.Received(1).SellabilityChangedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unresolved_Category_Writes_Nothing()
    {
        _categories.FindByCanonicalPathAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((Category?)null);

        var result = await CreateService().UpsertProductAsync(Command(), Ct);

        result.Error!.Code.Should().Be("PUBLISH_CATEGORY_UNRESOLVED");
        result.Error.HttpStatus.Should().Be(422);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _products.DidNotReceive().AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task New_Product_Without_Usable_Image_Is_Not_Saved()
    {
        _products.FindByIdentifierAsync(
            Arg.Any<ProductIdentifierType>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns((Product?)null);

        var command = Command(media: [new PublishMedia(
            "staging/kettle.png", "kettle", "https://example/kettle.png",
            MediaUsageRights.RightsUnknown, 0)]);

        var result = await CreateService().UpsertProductAsync(command, Ct);

        result.Error!.Code.Should().Be("PRODUCT_ACTIVATION_INVALID");
        result.Error.Message.Should().Contain("NO_USABLE_IMAGE");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _products.DidNotReceive().AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Variant_An_Admin_Switched_Off_Stays_Off_When_The_Source_Lists_It_Again()
    {
        var existing = ActiveProductWithGtin(Gtin);
        var variantId = existing.Variants.Single().Id;
        existing.SetVariantActive(variantId, false, byAdmin: true, _clock.UtcNow);
        MatchesGtin(Gtin, existing);

        await CreateService().UpsertProductAsync(Command(), Ct);

        existing.Variants.Single().IsActive.Should().BeFalse(
            "the source listing it again is not news the admin has not already seen");
    }

    [Fact]
    public async Task A_Brand_That_Cannot_Be_Resolved_Stops_The_Publish()
    {
        _brands.EnsureAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<long>(new Error("BRAND_NAME_REQUIRED", "Brand name is required.")));

        var result = await CreateService().UpsertProductAsync(Command(), Ct);

        result.Error!.Code.Should().Be("BRAND_NAME_REQUIRED");
        await _categories.DidNotReceiveWithAnyArgs().FindByCanonicalPathAsync(default!, default);
    }

    [Fact]
    public async Task An_Unparseable_Identifier_Matches_Nothing_Rather_Than_Failing_The_Import()
    {
        _products.FindByIdentifierAsync(
            Arg.Any<ProductIdentifierType>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns((Product?)null);

        // A GTIN with a bad check digit: it identifies nothing, so matching skips it. The
        // aggregate then refuses to store it, which is where the feed row is reported.
        var result = await CreateService().UpsertProductAsync(Command(gtin: "4006381333932"), Ct);

        result.Error!.Code.Should().Be("IDENTIFIER_CHECK_DIGIT_INVALID");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_New_Variant_On_An_Existing_Product_Is_Reported_As_Created()
    {
        var existing = ActiveProductWithGtin(Gtin);
        MatchesGtin(Gtin, existing);

        var command = Command() with
        {
            Variants = [new PublishVariant(new Dictionary<string, string> { ["Size"] = "1.0L" }, null)],
        };

        var result = await CreateService().UpsertProductAsync(command, Ct);

        result.Value!.Created.Should().BeFalse();
        result.Value.Variants.Should().ContainSingle().Which.Created.Should().BeTrue();
        existing.Variants.Should().HaveCount(2);
    }
}
