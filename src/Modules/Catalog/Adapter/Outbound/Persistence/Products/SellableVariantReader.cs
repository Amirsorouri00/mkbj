using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Persistence.Products;

internal sealed class SellableVariantReader(EwaysVNextDbContext db) : ISellableVariantReader
{
    public async Task<IReadOnlyList<SellableVariant>> ReadSellableAsync(
        IReadOnlyCollection<long>? variantIds, CancellationToken ct = default)
    {
        // Three conditions, one query. Sellable means the product is live, this variant of it is
        // live, and the brand behind it is live — a deactivated brand takes its whole catalogue
        // off sale, and joining here is what makes that true without a second round trip.
        var query =
            from variant in db.Set<Variant>().AsNoTracking()
            join product in db.Products.AsNoTracking() on variant.ProductId equals product.Id
            join brand in db.Brands.AsNoTracking() on product.BrandId equals brand.Id
            where variant.IsActive
                  && product.Status == ProductStatus.Active
                  && brand.IsActive
            select new
            {
                variant.Id,
                variant.ProductId,
                Sku = variant.Sku,
                product.Title,
                product.BrandId,
                BrandName = brand.DisplayName,
                product.PrimaryCategoryId,
                variant.AttributeValues,
            };

        if (variantIds is { Count: > 0 })
        {
            query = query.Where(row => variantIds.Contains(row.Id));
        }

        var rows = await query.ToListAsync(ct);
        if (rows.Count == 0) return [];

        var paths = await ReadCategoryPathsAsync(ct);

        return rows
            .Select(row => new SellableVariant(
                row.Id,
                row.ProductId,
                row.Sku.Value,
                row.Title,
                row.BrandId,
                row.BrandName,
                row.PrimaryCategoryId,
                paths.TryGetValue(row.PrimaryCategoryId, out var path) ? path : [],
                row.AttributeValues))
            .OrderBy(variant => variant.VariantId)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<long, VariantState>> ReadStatesAsync(
        IReadOnlyCollection<long> variantIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(variantIds);
        if (variantIds.Count == 0) return new Dictionary<long, VariantState>();

        var rows = await (
            from variant in db.Set<Variant>().AsNoTracking()
            join product in db.Products.AsNoTracking() on variant.ProductId equals product.Id
            join brand in db.Brands.AsNoTracking() on product.BrandId equals brand.Id
            where variantIds.Contains(variant.Id)
            select new
            {
                variant.Id,
                variant.ProductId,
                Sellable = variant.IsActive && product.Status == ProductStatus.Active && brand.IsActive,
            }).ToListAsync(ct);

        var states = rows.ToDictionary(
            row => row.Id,
            row => new VariantState(row.Id, row.ProductId, Exists: true, row.Sellable));

        // An id with no row is reported as not existing rather than left out. A caller checking
        // a list of ids needs an answer for every one of them; a missing key reads as "I forgot
        // to ask", which is a different thing from "it is not there".
        foreach (var missing in variantIds.Where(id => !states.ContainsKey(id)))
        {
            states[missing] = new VariantState(missing, 0, Exists: false, IsSellable: false);
        }

        return states;
    }

    /// <summary>
    /// Every category's root-to-leaf name path, assembled in memory.
    /// <para>
    /// The whole table is read because it is small — a few hundred rows at most — and walking
    /// parents in SQL would cost one recursive query per product. Assembled once per call rather
    /// than cached, since a stale breadcrumb outlives the rename that caused it.
    /// </para>
    /// </summary>
    private async Task<Dictionary<long, IReadOnlyList<string>>> ReadCategoryPathsAsync(CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking()
            .Select(category => new { category.Id, category.ParentId, category.Name })
            .ToDictionaryAsync(category => category.Id, ct);

        var paths = new Dictionary<long, IReadOnlyList<string>>(categories.Count);

        foreach (var id in categories.Keys)
        {
            var names = new List<string>();
            var seen = new HashSet<long>();
            var current = (long?)id;

            // The hop guard is for a cycle a direct database write could leave behind; the
            // aggregate refuses to create one, but this read must not hang on a row that got
            // past it.
            while (current is { } categoryId && seen.Add(categoryId) &&
                   categories.TryGetValue(categoryId, out var category))
            {
                names.Add(category.Name);
                current = category.ParentId;
            }

            names.Reverse();
            paths[id] = names;
        }

        return paths;
    }
}
