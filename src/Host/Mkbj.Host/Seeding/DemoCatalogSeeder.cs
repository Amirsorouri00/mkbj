using Mkbj.Core.Application;
using Mkbj.Modules.Catalog.Application.AttributeBlueprints.Domain;
using Mkbj.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;
using Mkbj.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using Mkbj.Modules.Catalog.Application.Categories.Commands;
using Mkbj.Modules.Catalog.Application.Categories.Ports.Inbound;
using Mkbj.Modules.Catalog.Application.Categories.Ports.Outbound;
using Mkbj.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Inbound;
using Mkbj.Modules.Catalog.Application.Products.Domain;
using Mkbj.Modules.Catalog.Application.Products.Ports.Inbound;
using Mkbj.Modules.Catalog.QueryApplication.AttributeBlueprints;
using Mkbj.Modules.Pricing.Application.Fx.Ports.Inbound;
using Mkbj.Modules.Pricing.Application.Prices.Domain;
using Mkbj.Modules.Pricing.Application.Prices.Ports.Inbound;
using Mkbj.Outcomes;
using Mkbj.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mkbj.Host.Seeding;

public sealed record SeedSummary(int Categories, int Products, int Variants, int Prices);

/// <summary>
/// Fills an empty shop with a working demo catalogue.
/// <para>
/// Everything goes through the ordinary ports — the category service, the publishing port, the
/// price publishing port. A seeder that wrote rows directly would drift from what those paths
/// actually do, and the demo would stop being evidence that they work.
/// </para>
/// </summary>
public static class DemoCatalogSeeder
{
    private const string Provenance = "seed";

    /// <summary>What the demo claims as a source, so a seeded price is never mistaken for an observed one.</summary>
    private const string SeedSourceUrl = "seed:demo";

    public static async Task<Result<SeedSummary>> RunAsync(
        IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoCatalogSeeder));

        var categories = await EnsureCategoriesAsync(services, ct);
        if (categories.IsFailure) return Result.Failure<SeedSummary>(categories.Error!);

        var attributes = await EnsureCategoryAttributesAsync(services, ct);
        if (attributes.IsFailure) return Result.Failure<SeedSummary>(attributes.Error!);

        await EnsureRatesAsync(services, logger, ct);

        var publishing = services.GetRequiredService<ICatalogPublishingPort>();
        var prices = services.GetRequiredService<IPricePublishingPort>();
        var storage = services.GetRequiredService<IObjectStorage>();

        // Twelve curated, locally owned catalogue images keep the demo useful offline and avoid
        // brittle third-party URLs. One category image is staged once and reused by that
        // category's products; publishing still copies it through the ordinary media port.
        foreach (var category in DemoCatalogData.Categories)
        {
            await using var image = OpenSeedImage(category.Root);
            await storage.PutAsync(
                StorageBuckets.ImportStaging,
                SeedImageKey(category.Root),
                image,
                "image/jpeg",
                ct);
        }

        var products = 0;
        var variants = 0;
        var priced = 0;

        foreach (var product in DemoCatalogData.Products)
        {
            var stagingKey = SeedImageKey(product.Category.Root);

            var published = await publishing.UpsertProductAsync(
                new PublishProductCommand(
                    new PublishBrand(product.Brand, []),
                    [product.Category.Root, product.Category.Child],
                    product.Title,
                    DemoCatalogData.ShortDescription,
                    null,
                    [new IdentifierInput(ProductIdentifierType.Gtin, product.Gtin, null)],
                    [new PublishMedia(stagingKey, product.Title, $"{SeedSourceUrl}/{product.Gtin}",
                        MediaUsageRights.Allowed, 0)],
                    product.Variants
                        .Select(variant => new PublishVariant(variant.Attributes, null))
                        .ToList(),
                    [],
                    Provenance),
                ct);

            if (published.IsFailure)
            {
                logger.LogWarning(
                    "Could not publish demo product {Gtin}: {Code}", product.Gtin, published.Error!.Code);
                continue;
            }

            products++;
            variants += published.Value!.Variants.Count;

            // Variants come back in the order they were sent, so each one's price is the one
            // beside it in the demo data.
            for (var index = 0; index < published.Value.Variants.Count; index++)
            {
                var demoVariant = product.Variants[index];
                var result = await prices.SetReferencePriceAsync(
                    published.Value.Variants[index].VariantId,
                    demoVariant.BaseAmountMinor,
                    demoVariant.BaseCurrency,
                    new PriceProvenance(null, SeedSourceUrl, DateTimeOffset.UtcNow, null, null, null),
                    ct);

                if (result.IsSuccess) priced++;
            }
        }

        return Result.Success(new SeedSummary(categories.Value, products, variants, priced));
    }

    private static string SeedImageKey(string category) =>
        $"seed/catalog/{category.ToLowerInvariant()}.jpg";

    private static Stream OpenSeedImage(string category)
    {
        var resource = $"Mkbj.Host.Seeding.Assets.{category.ToLowerInvariant()}.jpg";
        return typeof(DemoCatalogSeeder).Assembly.GetManifestResourceStream(resource)
               ?? throw new InvalidOperationException($"Seed image resource '{resource}' was not found.");
    }

    /// <summary>
    /// Creates each root and its child, then activates them. A category starts inactive, and an
    /// inactive category cannot be published into — so the seed has to say so explicitly.
    /// </summary>
    private static async Task<Result<int>> EnsureCategoriesAsync(
        IServiceProvider services, CancellationToken ct)
    {
        var service = services.GetRequiredService<ICategoryService>();
        var repository = services.GetRequiredService<ICategoryRepository>();
        var created = 0;

        foreach (var category in DemoCatalogData.Categories)
        {
            var rootId = await EnsureAsync(service, repository, category.Root, null, ct);
            if (rootId.IsFailure) return Result.Failure<int>(rootId.Error!);

            var childId = await EnsureAsync(service, repository, category.Child, rootId.Value, ct);
            if (childId.IsFailure) return Result.Failure<int>(childId.Error!);

            created += 2;
        }

        return Result.Success(created);
    }

    private static async Task<Result<long>> EnsureAsync(
        ICategoryService service, ICategoryRepository repository,
        string name, long? parentId, CancellationToken ct)
    {
        // Looked up by canonical path, which is what publishing resolves against — so a second
        // run finds exactly what the first one created.
        var path = parentId is null ? new[] { name } : [await RootKeyAsync(repository, parentId.Value, ct), name];
        var existing = await repository.FindByCanonicalPathAsync(path, ct);

        long categoryId;
        if (existing is null)
        {
            var slug = parentId is null
                ? name.ToLowerInvariant()
                : $"{name.ToLowerInvariant()}-{parentId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

            var created = await service.CreateAsync(
                new CreateCategoryCommand(name, slug, null, parentId, 0), ct);

            if (created.IsFailure) return Result.Failure<long>(created.Error!);
            categoryId = created.Value;
        }
        else
        {
            categoryId = existing.Id;
        }

        var activated = await service.SetStatusAsync(
            new SetCategoryStatusCommand(categoryId, Modules.Catalog.Contracts.Categories.CategoryStatus.Active), ct);

        return activated.IsFailure
            ? Result.Failure<long>(activated.Error!)
            : Result.Success(categoryId);
    }

    private static async Task<string> RootKeyAsync(
        ICategoryRepository repository, long rootId, CancellationToken ct) =>
        (await repository.GetByIdAsync(rootId, ct))?.CanonicalKey ?? string.Empty;

    /// <summary>
    /// Gives the deterministic demo catalogue the same category-driven variant axes used by its
    /// products. Assigning at each root deliberately exercises inheritance in every child.
    /// </summary>
    private static async Task<Result> EnsureCategoryAttributesAsync(
        IServiceProvider services, CancellationToken ct)
    {
        var blueprints = services.GetRequiredService<IAttributeBlueprintService>();
        var blueprintRepository = services.GetRequiredService<IAttributeBlueprintRepository>();
        var assignments = services.GetRequiredService<ICategoryAttributeAssignmentService>();
        var categories = services.GetRequiredService<ICategoryRepository>();

        var colour = await EnsureBlueprintAsync(
            blueprints, blueprintRepository, "Colour", ["Red", "Blue", "Green"], ct);
        if (colour.IsFailure) return Result.Failure(colour.Error!);

        var size = await EnsureBlueprintAsync(
            blueprints, blueprintRepository, "Size", ["S", "M", "L"], ct);
        if (size.IsFailure) return Result.Failure(size.Error!);

        foreach (var category in DemoCatalogData.Categories)
        {
            var root = await categories.FindByCanonicalPathAsync([category.Root], ct);
            if (root is null)
                return Result.Failure(new Error("CATEGORY_NOT_FOUND", $"Seed category '{category.Root}' was not found."));

            var current = await assignments.ListByCategoryAsync(root.Id, ct);
            if (current.IsFailure) return Result.Failure(current.Error!);
            foreach (var (blueprintId, inTitle, titleOrder, primary) in new[]
            {
                (colour.Value, true, (int?)1, true),
                (size.Value, false, (int?)null, false),
            })
            {
                if (current.Value?.Any(item => item.AttributeBlueprintId == blueprintId && !item.IsRemoved) == true)
                    continue;

                var assigned = await assignments.AssignAsync(
                    root.Id, blueprintId, isRequired: false, isFilterable: true,
                    isSearchable: true, inTitle, titleOrder, primary,
                    isVariantGenerating: true, ct);
                if (assigned.IsFailure) return Result.Failure(assigned.Error!);
            }
        }

        return Result.Success();
    }

    private static async Task<Result<long>> EnsureBlueprintAsync(
        IAttributeBlueprintService service,
        IAttributeBlueprintRepository repository,
        string name,
        IReadOnlyCollection<string> options,
        CancellationToken ct)
    {
        var existing = await repository.ListSnapshotsAsync(
            1, 100, includeDeleted: true, filter: new AttributeBlueprintFilter(name), ct: ct);
        var snapshot = existing.Items.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (snapshot is not null) return Result.Success(snapshot.Id);

        return await service.CreateAsync(
            name,
            AttributeBlueprintDataType.Option,
            [.. options.Select(value => new AttributeBlueprintOptionInput(value, null))],
            ct);
    }

    /// <summary>
    /// Fetches live rates, and falls back to the fixture's when offline.
    /// <para>
    /// A demo with no exchange rates shows every product unpriced in three of its five
    /// currencies, which looks like a bug in the shop rather than a machine with no network.
    /// The fallback batch is named <c>seed-demo</c> so nobody mistakes it for a published one.
    /// </para>
    /// </summary>
    private static async Task EnsureRatesAsync(
        IServiceProvider services, ILogger logger, CancellationToken ct)
    {
        var fx = services.GetRequiredService<IFxRefreshService>();

        var refreshed = await fx.RefreshAsync(ct);
        if (refreshed.IsSuccess) return;

        logger.LogWarning(
            "Could not reach the rate provider ({Code}); seeding fixed demo rates instead.",
            refreshed.Error!.Code);

        await fx.IngestAsync(
            new FxProviderBatch(
                "seed-demo",
                DateOnly.FromDateTime(DateTime.UtcNow),
                [
                    new FxQuote("USD", 1.0812m),
                    new FxQuote("JPY", 163.42m),
                    new FxQuote("GBP", 0.84215m),
                    new FxQuote("SGD", 1.4321m),
                ]),
            ct);
    }
}
