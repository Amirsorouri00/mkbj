using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Gateways;

/// <summary>
/// Asks the catalogue whether it already has this product.
/// </summary>
/// <remarks>
/// <para>
/// Reads the catalogue's tables directly through the shared context rather than through a
/// Catalog service, because this is a query over hundreds of thousands of rows and the answer
/// has to come from the database. A service returning candidates would either page them or
/// materialise the lot.
/// </para>
/// <para>
/// Similarity is computed by <c>pg_trgm</c>, which the model already enables and the storefront
/// already indexes. Doing it in this process would mean reading every title out of the database
/// to compare — slower than the import it is meant to speed up.
/// </para>
/// </remarks>
internal sealed class CatalogMatcherGateway(EwaysVNextDbContext db) : ICatalogMatcher
{
    public async Task<long?> FindByIdentifierAsync(
        string identifierType, string value, CancellationToken ct = default)
    {
        if (!Enum.TryParse<ProductIdentifierType>(identifierType, ignoreCase: true, out var type))
            return null;

        // A GTIN means the same thing wherever it came from, so no domain is involved. An exact
        // identifier match is a fact rather than a suggestion, which is why it returns one id
        // and not a list.
        return await db.Products
            .AsNoTracking()
            .Where(product => product.Identifiers.Any(identifier =>
                identifier.Type == type && identifier.Value == value))
            .Select(product => (long?)product.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<CatalogCandidate>> FindSimilarAsync(
        string title, double threshold, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title)) return [];

        var rows = await db.Products
            .AsNoTracking()
            .Select(product => new
            {
                product.Id,
                product.Title,
                Similarity = EF.Functions.TrigramsSimilarity(product.Title, title),
            })
            .Where(row => row.Similarity >= threshold)
            .OrderByDescending(row => row.Similarity)
            .ThenBy(row => row.Id)
            .Take(limit)
            .ToListAsync(ct);

        return [.. rows.Select(row => new CatalogCandidate(row.Id, row.Title, row.Similarity))];
    }
}
