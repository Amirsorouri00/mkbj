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
/// The admin write path for products. Most of what is asserted here is ordering: nothing is
/// announced before it is saved, nothing is half-changed when a check fails, and a cart is only
/// told its contents went stale when they actually did.
/// </summary>
public sealed class CatalogAdminServiceTests
{
    private const long ProductId = 500;
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

    public CatalogAdminServiceTests()
    {
        _brands.ExistsAsync(BrandId, Arg.Any<CancellationToken>()).Returns(true);
        _categories.GetByIdAsync(CategoryId, Arg.Any<CancellationToken>())
            .Returns(Category.Create(CategoryId, "Kettles", "kettles", null, null, 0, _clock.UtcNow));
        _ids.Generate().Returns(_ => Random.Shared.NextInt64(1_000, long.MaxValue));
        _unitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(SaveOutcome.Saved);
    }

    private CatalogAdminService CreateService() => new(
        _products, _media, _fileTypes, _mediaOptions, _logger, _brands, _categories, _categoryProducts, _audit, _notifier, _unitOfWork, _ids, _clock);

    private Product Draft(string title = "Demo Kettle") =>
        Product.CreateDraft(ProductId, BrandId, CategoryId, title, "demo-kettle", null, null, _clock.UtcNow);

    /// <summary>Active, with one variant (id 2) and one usable image.</summary>
    private Product Active()
    {
        var product = Draft();
        product.AddVariant(2, "SKU-0002", new Dictionary<string, string>(), _clock.UtcNow);
        product.AddMedia(3, "products/500/3.png", 0, null, null, MediaUsageRights.Allowed, _clock.UtcNow);
        product.Activate(byAdmin: true, _clock.UtcNow);
        return product;
    }

    private void Loaded(Product product) =>
        _products.GetByIdAsync(ProductId, Arg.Any<CancellationToken>()).Returns(product);

    private static CreateProductCommand CreateCommand(long brandId = BrandId, long categoryId = CategoryId) =>
        new(brandId, categoryId, "Demo Kettle", null, null, null);

    [Fact]
    public async Task CreateProduct_Saves_Draft_And_Audits()
    {
        var result = await CreateService().CreateProductAsync(CreateCommand(), _actor, Ct);

        result.IsSuccess.Should().BeTrue();
        await _products.Received(1).AddAsync(
            Arg.Is<Product>(product => product.Status == ProductStatus.Draft), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).TrySaveChangesAsync(Arg.Any<CancellationToken>());
        _audit.Received(1).Record(
            Arg.Any<AuditActor>(), "product.created", "Product", Arg.Any<string>(),
            Arg.Is<object?>(before => before == null), Arg.Any<object>());

        await _notifier.DidNotReceiveWithAnyArgs().SellabilityChangedAsync();
    }

    [Fact]
    public async Task CreateProduct_Rejects_Unknown_Brand_Before_Saving()
    {
        _brands.ExistsAsync(999, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateService().CreateProductAsync(CreateCommand(brandId: 999), _actor, Ct);

        result.Error!.Code.Should().Be("BRAND_NOT_FOUND");
        result.Error.HttpStatus.Should().Be(404);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateProduct_Rejects_Unknown_Category_Before_Saving()
    {
        _categories.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await CreateService().CreateProductAsync(CreateCommand(categoryId: 999), _actor, Ct);

        result.Error!.Code.Should().Be("CATEGORY_NOT_FOUND");
        await _products.DidNotReceive().AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateProduct_Rejects_A_Slug_Already_In_Use()
    {
        _products.GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Draft());

        var result = await CreateService().CreateProductAsync(CreateCommand(), _actor, Ct);

        result.Error!.Code.Should().Be("PRODUCT_SLUG_DUPLICATE");
        result.Error.HttpStatus.Should().Be(409);
    }

    [Fact]
    public async Task AddVariant_Rejects_Sku_Used_By_Another_Product()
    {
        Loaded(Draft());
        _products.SkuExistsAsync("SKU-TAKEN", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().AddVariantAsync(
            new AddVariantCommand(ProductId, "SKU-TAKEN", new Dictionary<string, string>()), _actor, Ct);

        result.Error!.Code.Should().Be("VARIANT_SKU_DUPLICATE");
        await _unitOfWork.DidNotReceive().TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddVariant_Without_A_Sku_Derives_One_From_The_Variant_Id()
    {
        var product = Draft();
        Loaded(product);

        var result = await CreateService().AddVariantAsync(
            new AddVariantCommand(ProductId, null, new Dictionary<string, string>()), _actor, Ct);

        result.IsSuccess.Should().BeTrue();
        product.Variants.Single().Sku.Value.Should().Be(
            $"SKU-{result.Value}",
            "the id is already unique, so a derived SKU cannot collide");
    }

    [Fact]
    public async Task SetProductActive_False_Marks_DeactivatedByAdmin_And_Bumps_Version_After_Save()
    {
        var product = Active();
        Loaded(product);

        var result = await CreateService().SetProductActiveAsync(ProductId, false, _actor, Ct);

        result.IsSuccess.Should().BeTrue();
        product.DeactivatedByAdmin.Should().BeTrue(
            "otherwise the next import run would switch it straight back on");

        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _notifier.SellabilityChangedAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task SetProductActive_True_Returns_Activation_Problems()
    {
        var product = Draft();
        product.AddVariant(2, "SKU-0002", new Dictionary<string, string>(), _clock.UtcNow);
        Loaded(product);

        var result = await CreateService().SetProductActiveAsync(ProductId, true, _actor, Ct);

        result.Error!.Code.Should().Be("PRODUCT_ACTIVATION_INVALID");
        result.Error.Message.Should().Contain("NO_USABLE_IMAGE",
            "the admin has to be told what is missing, not just that something is");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unique_Violation_On_Save_Maps_To_Conflict_For_An_Identifier()
    {
        Loaded(Draft());
        _unitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(SaveOutcome.UniqueViolation);

        var result = await CreateService().AddIdentifierAsync(
            ProductId, new IdentifierInput(ProductIdentifierType.Gtin, "4006381333931", null), _actor, Ct);

        result.Error!.Code.Should().Be("PRODUCT_IDENTIFIER_DUPLICATE",
            "the identifier indexes span every product, so only the database can answer this");
        result.Error.HttpStatus.Should().Be(409);
    }

    [Fact]
    public async Task Unique_Violation_On_Save_Maps_To_Conflict_For_A_Variant()
    {
        Loaded(Draft());
        _unitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(SaveOutcome.UniqueViolation);

        var result = await CreateService().AddVariantAsync(
            new AddVariantCommand(ProductId, "SKU-RACED", new Dictionary<string, string>()), _actor, Ct);

        result.Error!.Code.Should().Be("VARIANT_SKU_DUPLICATE",
            "another admin can claim the SKU between the pre-check and the save");
    }

    [Fact]
    public async Task A_Missing_Product_Is_Reported_Rather_Than_Crashing()
    {
        _products.GetByIdAsync(ProductId, Arg.Any<CancellationToken>()).Returns((Product?)null);

        var result = await CreateService().SetProductActiveAsync(ProductId, true, _actor, Ct);

        result.Error!.Code.Should().Be("PRODUCT_NOT_FOUND");
        result.Error.HttpStatus.Should().Be(404);
    }

    [Fact]
    public async Task Updating_A_Draft_Never_Bumps_The_Version()
    {
        Loaded(Draft());
        _categories.GetByIdAsync(999, Arg.Any<CancellationToken>())
            .Returns(Category.Create(999, "Other", "other", null, null, 0, _clock.UtcNow));

        // A different category, which on a live product would bump the version. On a draft it
        // cannot: nothing has ever been able to hold one.
        var result = await CreateService().UpdateProductAsync(
            new UpdateProductCommand(ProductId, BrandId, 999, "Renamed", null, null), _actor, Ct);

        result.IsSuccess.Should().BeTrue();
        await _notifier.DidNotReceiveWithAnyArgs().SellabilityChangedAsync();
    }

    [Fact]
    public async Task Moving_A_Live_Product_To_Another_Brand_Bumps_The_Version()
    {
        Loaded(Active());
        _brands.ExistsAsync(77, Arg.Any<CancellationToken>()).Returns(true);

        await CreateService().UpdateProductAsync(
            new UpdateProductCommand(ProductId, 77, CategoryId, "Demo Kettle", null, null), _actor, Ct);

        await _notifier.Received(1).SellabilityChangedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retitling_A_Live_Product_Only_Notifies_A_Display_Change()
    {
        Loaded(Active());

        await CreateService().UpdateProductAsync(
            new UpdateProductCommand(ProductId, BrandId, CategoryId, "Demo Kettle Mk II", null, null),
            _actor, Ct);

        await _notifier.Received(1).DisplayChangedAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().SellabilityChangedAsync();
    }

    [Fact]
    public async Task SetSecondaryCategories_Replaces_The_Set_And_Ignores_The_Primary()
    {
        Loaded(Draft());
        _categoryProducts.ListCategoryIdsForProductAsync(ProductId, Arg.Any<CancellationToken>())
            .Returns([31L, 32L]);
        foreach (var id in new[] { 31L, 32L, 33L })
        {
            _categories.GetByIdAsync(id, Arg.Any<CancellationToken>())
                .Returns(Category.Create(id, $"C{id}", $"c{id}", null, null, 0, _clock.UtcNow));
        }

        var result = await CreateService().SetSecondaryCategoriesAsync(
            ProductId, [32L, 33L, CategoryId], _actor, Ct);

        result.IsSuccess.Should().BeTrue();
        await _categoryProducts.Received(1).RemoveIfPresentAsync(31L, ProductId, Arg.Any<CancellationToken>());
        await _categoryProducts.Received(1).AddIfAbsentAsync(
            Arg.Is<CategoryProduct>(assignment => assignment.CategoryId == 33L), Arg.Any<CancellationToken>());
        await _categoryProducts.DidNotReceive().AddIfAbsentAsync(
            Arg.Is<CategoryProduct>(assignment => assignment.CategoryId == CategoryId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetSecondaryCategories_Rejects_An_Unknown_Category_Before_Writing()
    {
        Loaded(Draft());
        _categories.GetByIdAsync(404L, Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await CreateService().SetSecondaryCategoriesAsync(ProductId, [404L], _actor, Ct);

        result.Error!.Code.Should().Be("CATEGORY_NOT_FOUND");
        await _categoryProducts.DidNotReceiveWithAnyArgs().AddIfAbsentAsync(default!, default);
    }

    [Fact]
    public async Task Removing_An_Identifier_That_Is_Not_There_Is_Reported()
    {
        Loaded(Draft());

        var result = await CreateService().RemoveIdentifierAsync(ProductId, 999, _actor, Ct);

        result.Error!.Code.Should().Be("PRODUCT_IDENTIFIER_NOT_FOUND");
        result.Error.HttpStatus.Should().Be(404,
            "SuggestHttpStatus reads the NOT_FOUND suffix, so a domain code lands on the right status");
    }

    [Fact]
    public async Task Deactivating_A_Variant_Is_Recorded_As_An_Admin_Decision()
    {
        var product = Active();
        Loaded(product);

        await CreateService().SetVariantActiveAsync(ProductId, 2, false, _actor, Ct);

        product.Variants.Single().DeactivatedByAdmin.Should().BeTrue();
        await _notifier.Received(1).SellabilityChangedAsync(Arg.Any<CancellationToken>());
    }
}
