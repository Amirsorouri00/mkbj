using EwaysVNext.Core.Application;
using EwaysVNext.Database;
using Microsoft.EntityFrameworkCore;
using EwaysVNext.Host.Seeding;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Adapter.Outbound.Storefront;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Commands;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.IntegrationTests.Support;

/// <summary>
/// A small shop, built through the real admin services, that every storefront test shares.
/// </summary>
/// <remarks>
/// The root category carries the filterable <c>colour</c> assignment. P1 has two variants, Red
/// and Blue, priced in USD; P2 has one priced in EUR, so conversion is exercised; P3 is Draft and
/// priced, because a product with a price is not a product for sale; P4 was active once and was
/// then taken down by an admin, which is a different state from never having been active.
/// </remarks>
public sealed record StorefrontScenario(
    long RootCategoryId,
    long ChildCategoryId,
    long BrandAId,
    long BrandBId,
    long P1,
    long P1Red,
    long P1Blue,
    long P2,
    long P2Variant,
    long P3,
    long P3Variant,
    long P4,
    long P4Variant,
    AdminActor Staff,
    AdminActor Super)
{
    /// <summary>
    /// Builds the scenario under category names unique to this call, and refreshes the views.
    /// </summary>
    /// <remarks>
    /// Unique names because the suite shares one database: a fixed <c>Kettles</c> would collide
    /// with the previous test's, and a facet count would then be counting someone else's
    /// products.
    /// </remarks>
    public static async Task<StorefrontScenario> CreateAsync(
        IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var staff = new AdminActor(Guid.NewGuid(), IsSuperAdmin: false);
        var super = new AdminActor(Guid.NewGuid(), IsSuperAdmin: true);

        var categories = services.GetRequiredService<ICategoryService>();
        var attributes = services.GetRequiredService<IAttributeBlueprintService>();
        var assignments = services.GetRequiredService<ICategoryAttributeAssignmentService>();
        var brands = services.GetRequiredService<IBrandAdminService>();
        var catalog = services.GetRequiredService<ICatalogAdminService>();
        var prices = services.GetRequiredService<IPricePublishingPort>();

        var rootId = await ActiveCategoryAsync(categories, $"sf-{suffix}", null, ct);
        var childId = await ActiveCategoryAsync(categories, $"kettles-{suffix}", rootId, ct);

        // Named "colour" exactly, not suffixed: the facet view matches a blueprint's canonical
        // key against the key stored on the variant, so a suffixed blueprint would never line up
        // with a "colour" attribute and the attribute facets would silently come back empty.
        // Blueprints are global, so this one is shared with whatever earlier test created it.
        var colourId = await ColourBlueprintAsync(services, attributes, ct);

        // On the root, because the view looks for a filterable assignment anywhere on the
        // variant's ancestor path — inheriting down the tree is the behaviour under test. The
        // assignment is per category, so it is this scenario's own.

        Succeeded(await assignments.AssignAsync(
            rootId, colourId, isRequired: false, isFilterable: true, isSearchable: false,
            isInTitle: false, titleOrder: null, isPrimary: false, isVariantGenerating: true, ct));

        var brandA = Value(await brands.CreateAsync($"Steelworks {suffix}", [], staff, ct));
        var brandB = Value(await brands.CreateAsync($"Glassworks {suffix}", [], staff, ct));

        var (p1, p1Red) = await ProductAsync(catalog, brandA, childId, $"Steel Kettle {suffix}",
            new Dictionary<string, string> { ["colour"] = "Red" }, staff, ct);
        var p1Blue = Value(await catalog.AddVariantAsync(
            new AddVariantCommand(p1, null, new Dictionary<string, string> { ["colour"] = "Blue" }),
            staff, ct));

        var (p2, p2Variant) = await ProductAsync(catalog, brandB, childId, $"Glass Kettle {suffix}",
            new Dictionary<string, string>(), staff, ct);
        var (p3, p3Variant) = await ProductAsync(catalog, brandB, childId, $"Draft Kettle {suffix}",
            new Dictionary<string, string>(), staff, ct);
        var (p4, p4Variant) = await ProductAsync(catalog, brandA, childId, $"Retired Kettle {suffix}",
            new Dictionary<string, string>(), staff, ct);

        await PriceAsync(prices, p1Red, 2000, "USD", ct);
        await PriceAsync(prices, p1Blue, 2500, "USD", ct);
        await PriceAsync(prices, p2Variant, 3000, "EUR", ct);
        await PriceAsync(prices, p3Variant, 1500, "USD", ct);
        await PriceAsync(prices, p4Variant, 1800, "USD", ct);

        // P1, P2 and P4 go live; P3 stays Draft. P4 is then taken down by an admin, which is a
        // different state from never having been active and the views must treat it the same.
        Succeeded(await catalog.SetProductActiveAsync(p1, active: true, staff, ct));
        Succeeded(await catalog.SetProductActiveAsync(p2, active: true, staff, ct));
        Succeeded(await catalog.SetProductActiveAsync(p4, active: true, staff, ct));
        Succeeded(await catalog.SetProductActiveAsync(p4, active: false, staff, ct));

        var fx = services.GetRequiredService<IFxRefreshService>();
        await fx.IngestAsync(
            new FxProviderBatch("ecb", DateOnly.FromDateTime(DateTime.UtcNow),
                [new FxQuote("USD", 1.0812m), new FxQuote("SGD", 1.4321m), new FxQuote("GBP", 0.84215m)]),
            ct);

        await services.GetRequiredService<IStorefrontViewRefresher>().RefreshAllAsync(ct);

        return new StorefrontScenario(
            rootId, childId, brandA, brandB,
            p1, p1Red, p1Blue, p2, p2Variant, p3, p3Variant, p4, p4Variant, staff, super);
    }

    /// <summary>
    /// The one global <c>colour</c> blueprint, created on first use and reused afterwards.
    /// </summary>
    private static async Task<long> ColourBlueprintAsync(
        IServiceProvider services, IAttributeBlueprintService attributes, CancellationToken ct)
    {
        var db = services.GetRequiredService<EwaysVNextDbContext>();
        var existing = await db.AttributeBlueprints
            .FirstOrDefaultAsync(blueprint => blueprint.CanonicalKey == "colour", ct);

        if (existing is not null) return existing.Id;

        return Value(await attributes.CreateAsync(
            "colour", AttributeBlueprintDataType.Option,
            [new AttributeBlueprintOptionInput("Red", null), new AttributeBlueprintOptionInput("Blue", null)],
            ct));
    }

    private static async Task<long> ActiveCategoryAsync(
        ICategoryService categories, string name, long? parentId, CancellationToken ct)
    {
        var id = Value(await categories.CreateAsync(
            new CreateCategoryCommand(name, name, null, parentId, 0), ct));

        // A category is created inactive, and a product cannot be shown under an inactive one.
        Succeeded(await categories.SetStatusAsync(
            new SetCategoryStatusCommand(id, CategoryStatus.Active), ct));

        return id;
    }

    private static async Task<(long ProductId, long VariantId)> ProductAsync(
        ICatalogAdminService catalog, long brandId, long categoryId, string title,
        IReadOnlyDictionary<string, string> attributes, AdminActor actor, CancellationToken ct)
    {
        var productId = Value(await catalog.CreateProductAsync(
            new CreateProductCommand(brandId, categoryId, title, null, null, null), actor, ct));

        var variantId = Value(await catalog.AddVariantAsync(
            new AddVariantCommand(productId, null, attributes), actor, ct));

        // A product with no usable image cannot be activated, so every product here gets one —
        // generated rather than checked in, the same PNG the demo seed writes.
        var png = PlaceholderPng.Create(32, 32, 120, 120, 120);
        using var content = new MemoryStream(png);
        Succeeded(await catalog.UploadMediaAsync(
            new UploadMediaCommand(productId, content, png.Length, title, MediaUsageRights.Allowed, 0),
            actor, ct));

        return (productId, variantId);
    }

    private static async Task PriceAsync(
        IPricePublishingPort prices, long variantId, long minor, string currency, CancellationToken ct) =>
        Succeeded(await prices.SetReferencePriceAsync(
            variantId, minor, currency,
            new PriceProvenance(null, "test:storefront", DateTimeOffset.UtcNow, null, null, null),
            ct));

    private static long Value(EwaysVNext.Outcomes.Result<long> result) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error!.Message);

    private static void Succeeded(EwaysVNext.Outcomes.Result result)
    {
        if (result.IsFailure) throw new InvalidOperationException(result.Error!.Message);
    }

    private static void Succeeded<T>(EwaysVNext.Outcomes.Result<T> result)
    {
        if (result.IsFailure) throw new InvalidOperationException(result.Error!.Message);
    }
}
