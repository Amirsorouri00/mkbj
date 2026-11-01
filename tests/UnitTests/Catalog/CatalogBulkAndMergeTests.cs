using AwesomeAssertions;
using EwaysVNext.Audit;
using EwaysVNext.Core.Application.FileTypeDetection;
using EwaysVNext.Core.Implementation.FileTypeDetection;
using EwaysVNext.Modules.Catalog.Application.Products;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products.Services;
using EwaysVNext.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// Bulk edits and merges. A bulk edit is a batch of independent aggregates in one save — allowed
/// by rule 7 because no product's invariants depend on another's — and the thing worth pinning is
/// that one item failing neither fails the rest nor leaves that item half-changed.
/// </summary>
public sealed class CatalogBulkAndMergeTests
{
    private const long BrandId = 10;
    private const long CategoryId = 20;

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICatalogMediaStore _media = Substitute.For<ICatalogMediaStore>();
    private readonly IFileTypeDetector _fileTypes = new FileTypeDetector();
    private readonly IOptions<CatalogMediaOptions> _mediaOptions = Options.Create(new CatalogMediaOptions());
    private readonly ILogger<CatalogAdminService> _logger = NullLogger<CatalogAdminService>.Instance;
    private readonly IBrandRepository _brands = Substitute.For<IBrandRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly ICategoryProductRepository _categoryProducts = Substitute.For<ICategoryProductRepository>();
    private readonly IAuditTrail _audit = Substitute.For<IAuditTrail>();
    private readonly ICatalogChangeNotifier _notifier = Substitute.For<ICatalogChangeNotifier>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FixedClock _clock = new();
    private readonly AdminActor _actor = new(Guid.NewGuid(), IsSuperAdmin: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CatalogBulkAndMergeTests()
    {
        _brands.ExistsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);
        _categories.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => Category.Create(
                call.Arg<long>(), "Kettles", $"kettles-{call.Arg<long>()}", null, null, 0, _clock.UtcNow));
        _ids.Generate().Returns(_ => Random.Shared.NextInt64(1_000, long.MaxValue));
    }

    private CatalogAdminService CreateService() => new(
        _products, _media, _fileTypes, _mediaOptions, _logger, _brands, _categories, _categoryProducts, _audit, _notifier, _unitOfWork, _ids, _clock);

    private Product Draft(long id) =>
        Product.CreateDraft(id, BrandId, CategoryId, $"Product {id}", $"product-{id}", null, null, _clock.UtcNow);

    /// <summary>A product that would pass activation: one variant and one usable image.</summary>
    private Product Activatable(long id)
    {
        var product = Draft(id);
        product.AddVariant(id * 10, $"SKU-{id}", new Dictionary<string, string>(), _clock.UtcNow);
        product.AddMedia(id * 100, $"products/{id}/1.png", 0, null, null, MediaUsageRights.Allowed, _clock.UtcNow);
        return product;
    }

    private void Loads(params Product[] loaded) =>
        _products.GetByIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(loaded);

    [Fact]
    public async Task BulkEdit_Saves_Successful_Items_Once_And_Reports_Failures()
    {
        var first = Activatable(1);
        var second = Activatable(2);
        var third = Draft(3);   // no variant, no image: cannot go active
        Loads(first, second, third);

        var result = await CreateService().BulkEditAsync(
            new BulkProductEdit([1, 2, 3], BulkStatusChange.Activate, null, null), _actor, Ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEquivalentTo(new[]
        {
            new BulkItemOutcome(1, true, null),
            new BulkItemOutcome(2, true, null),
            new BulkItemOutcome(3, false, "PRODUCT_ACTIVATION_INVALID"),
        }, options => options.WithStrictOrdering());

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        third.Status.Should().Be(ProductStatus.Draft,
            "a product that failed its check must be left exactly as it was");
        first.Status.Should().Be(ProductStatus.Active);
    }

    [Fact]
    public async Task BulkEdit_Leaves_A_Failed_Item_Unmoved_Even_When_The_Edit_Also_Changes_Category()
    {
        var failing = Draft(3);
        Loads(failing);

        await CreateService().BulkEditAsync(
            new BulkProductEdit([3], BulkStatusChange.Activate, PrimaryCategoryId: 99, BrandId: null),
            _actor, Ct);

        failing.PrimaryCategoryId.Should().Be(CategoryId,
            "the activation check runs before anything is written, so the move does not happen either");
    }

    [Fact]
    public async Task BulkEdit_Rejects_More_Than_500_Ids()
    {
        var ids = Enumerable.Range(1, 501).Select(i => (long)i).ToList();

        var result = await CreateService().BulkEditAsync(
            new BulkProductEdit(ids, BulkStatusChange.Deactivate, null, null), _actor, Ct);

        result.Error!.Code.Should().Be("VALIDATION_FAILED");
        await _products.DidNotReceiveWithAnyArgs().GetByIdsAsync(default!, default);
    }

    [Fact]
    public async Task Five_Hundred_Ids_Are_Accepted()
    {
        var ids = Enumerable.Range(1, 500).Select(i => (long)i).ToList();
        Loads();

        var result = await CreateService().BulkEditAsync(
            new BulkProductEdit(ids, BulkStatusChange.Deactivate, null, null), _actor, Ct);

        result.IsSuccess.Should().BeTrue("the limit is inclusive");
    }

    [Fact]
    public async Task BulkEdit_Rejects_Empty_Edit()
    {
        var result = await CreateService().BulkEditAsync(
            new BulkProductEdit([1], BulkStatusChange.None, null, null), _actor, Ct);

        result.Error!.Code.Should().Be("VALIDATION_FAILED");
        await _products.DidNotReceiveWithAnyArgs().GetByIdsAsync(default!, default);
    }

    [Fact]
    public async Task BulkEdit_Rejects_An_Unknown_Brand_For_The_Whole_Batch()
    {
        _brands.ExistsAsync(999, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateService().BulkEditAsync(
            new BulkProductEdit([1, 2], BulkStatusChange.None, null, 999), _actor, Ct);

        result.Error!.Code.Should().Be("BRAND_NOT_FOUND",
            "a bad brand is the admin's mistake about the edit, not a per-product failure");
    }

    [Fact]
    public async Task BulkEdit_Reports_A_Product_That_Is_Not_There()
    {
        Loads(Activatable(1));

        var result = await CreateService().BulkEditAsync(
            new BulkProductEdit([1, 404], BulkStatusChange.Deactivate, null, null), _actor, Ct);

        result.Value!.Items.Should().ContainEquivalentOf(new BulkItemOutcome(404, false, "PRODUCT_NOT_FOUND"));
    }

    [Fact]
    public async Task Merge_Deactivates_Duplicate_And_Leaves_Survivor_Untouched()
    {
        var duplicate = Activatable(1);
        var survivor = Activatable(2);
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(duplicate);
        _products.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(survivor);
        var survivorUntouchedAt = survivor.UpdatedAt;

        var result = await CreateService().MergeDuplicateAsync(1, 2, _actor, Ct);

        result.IsSuccess.Should().BeTrue();
        duplicate.MergedIntoProductId.Should().Be(2);
        duplicate.DeactivatedByAdmin.Should().BeTrue();
        duplicate.Status.Should().Be(ProductStatus.Deactivated);

        survivor.MergedIntoProductId.Should().BeNull();
        survivor.UpdatedAt.Should().Be(survivorUntouchedAt,
            "the survivor is read only to check it exists; moving its identifiers would put two "
            + "aggregates whose invariants depend on each other into one save");

        _audit.Received(1).Record(
            Arg.Any<AuditActor>(), "product.merged", "Product", "1", Arg.Any<object>(), Arg.Any<object>());
    }

    [Fact]
    public async Task Merge_Into_A_Merged_Survivor_Is_Rejected()
    {
        var duplicate = Activatable(1);
        var survivor = Activatable(2);
        survivor.MarkMergedInto(3, _clock.UtcNow);
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(duplicate);
        _products.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(survivor);

        var result = await CreateService().MergeDuplicateAsync(1, 2, _actor, Ct);

        result.Error!.Code.Should().Be("PRODUCT_MERGE_INVALID",
            "a chain leaves every reader walking it, and the next merge could close it into a loop");
        duplicate.MergedIntoProductId.Should().BeNull();
    }

    [Fact]
    public async Task Merging_A_Product_Into_Itself_Is_Rejected()
    {
        var product = Activatable(1);
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);

        var result = await CreateService().MergeDuplicateAsync(1, 1, _actor, Ct);

        result.Error!.Code.Should().Be("PRODUCT_MERGE_INVALID");
    }

    [Fact]
    public async Task Merging_Reports_A_Missing_Survivor()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Activatable(1));
        _products.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns((Product?)null);

        var result = await CreateService().MergeDuplicateAsync(1, 2, _actor, Ct);

        result.Error!.Code.Should().Be("PRODUCT_NOT_FOUND");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Merging_Bumps_The_Sellability_Version_After_The_Save()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(Activatable(1));
        _products.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(Activatable(2));

        await CreateService().MergeDuplicateAsync(1, 2, _actor, Ct);

        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _notifier.SellabilityChangedAsync(Arg.Any<CancellationToken>());
        });
    }
}
