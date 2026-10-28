using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;
using EwaysVNext.Persistence;
using EwaysVNext.Queries.Application.Paginations;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Catalog.Adapter.Outbound.Repositories.AttributeBlueprints;

internal sealed class AttributeBlueprintRepository(EwaysVNextDbContext db) : EfCoreRepository<AttributeBlueprint, long>(db), IAttributeBlueprintRepository
{
    public new async Task<AttributeBlueprint?> GetByIdAsync(long id, CancellationToken ct = default)
        => await db.AttributeBlueprints
            .Include(blueprint => blueprint.Options)
            .FirstOrDefaultAsync(blueprint => blueprint.Id == id, ct);

    public async Task<bool> NameExistsAsync(string name, long? excludingId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var normalizedName = name.Trim();
        return await db.AttributeBlueprints.AnyAsync(blueprint =>
            !blueprint.DeletedAt.HasValue &&
            blueprint.Name == normalizedName &&
            (!excludingId.HasValue || blueprint.Id != excludingId.Value), ct);
    }

    public async Task<AttributeBlueprintSnapshot?> GetSnapshotByIdAsync(long id, CancellationToken ct = default)
        => await db.AttributeBlueprints.Where(blueprint => blueprint.Id == id)
            .Select(blueprint => new AttributeBlueprintSnapshot(
                blueprint.Id,
                blueprint.Name,
                blueprint.DataType.ToString(),
                blueprint.Status.ToString(),
                blueprint.DeletedAt,
                blueprint.CreatedAt,
                blueprint.UpdatedAt,
                blueprint.Options
                    .OrderBy(option => option.Id)
                    .Select(option => new AttributeBlueprintOptionSnapshot(
                        option.Id,
                        option.Value,
                        option.MeasurementUnitId))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

    public async Task<PaginatedQuery<AttributeBlueprintSnapshot>> ListSnapshotsAsync(int page, int pageSize, bool includeDeleted = false, AttributeBlueprintFilter? filter = null, CancellationToken ct = default)
    {
        var query = db.AttributeBlueprints.AsQueryable();
        if (includeDeleted) query = query.IgnoreQueryFilters();

        var name = filter?.NameContains?.Trim();
        if (!string.IsNullOrEmpty(name))
            query = query.Where(blueprint => blueprint.Name.Contains(name));

        if (filter?.Status is { } status)
        {
            var domainStatus = MapStatus(status);
            query = query.Where(blueprint => blueprint.Status == domainStatus);
        }

        if (filter?.DataType is { } dataType)
        {
            var domainDataType = MapDataType(dataType);
            query = query.Where(blueprint => blueprint.DataType == domainDataType);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query.OrderBy(blueprint => blueprint.Name)
                .ThenBy(blueprint => blueprint.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(blueprint => new AttributeBlueprintSnapshot(
                    blueprint.Id,
                    blueprint.Name,
                    blueprint.DataType.ToString(),
                    blueprint.Status.ToString(),
                    blueprint.DeletedAt,
                    blueprint.CreatedAt,
                    blueprint.UpdatedAt,
                    blueprint.Options
                        .OrderBy(option => option.Id)
                        .Select(option => new AttributeBlueprintOptionSnapshot(
                            option.Id,
                            option.Value,
                            option.MeasurementUnitId))
                        .ToList()))
                .ToListAsync(ct);

        var pageCount = (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PaginatedQuery<AttributeBlueprintSnapshot>(page, pageSize, pageCount, totalCount, items);
    }

    public async Task<IReadOnlyList<AttributeBlueprintSnapshot>> GetSnapshotsByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return Array.Empty<AttributeBlueprintSnapshot>();

        return await db.AttributeBlueprints
            .Where(blueprint => ids.Contains(blueprint.Id))
            .Select(blueprint => new AttributeBlueprintSnapshot(
                blueprint.Id,
                blueprint.Name,
                blueprint.DataType.ToString(),
                blueprint.Status.ToString(),
                blueprint.DeletedAt,
                blueprint.CreatedAt,
                blueprint.UpdatedAt,
                blueprint.Options
                    .OrderBy(option => option.Id)
                    .Select(option => new AttributeBlueprintOptionSnapshot(
                        option.Id,
                        option.Value,
                        option.MeasurementUnitId))
                    .ToList()))
            .ToListAsync(ct);
    }

    private static AttributeBlueprintStatus MapStatus(AttributeBlueprintStatusFilter status) => status switch
    {
        AttributeBlueprintStatusFilter.Active => AttributeBlueprintStatus.Active,
        AttributeBlueprintStatusFilter.Inactive => AttributeBlueprintStatus.Inactive,
        AttributeBlueprintStatusFilter.Archived => AttributeBlueprintStatus.Archived,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported attribute blueprint status.")
    };

    private static AttributeBlueprintDataType MapDataType(AttributeBlueprintDataTypeFilter dataType) => dataType switch
    {
        AttributeBlueprintDataTypeFilter.Text => AttributeBlueprintDataType.Text,
        AttributeBlueprintDataTypeFilter.Number => AttributeBlueprintDataType.Number,
        AttributeBlueprintDataTypeFilter.Boolean => AttributeBlueprintDataType.Boolean,
        AttributeBlueprintDataTypeFilter.Date => AttributeBlueprintDataType.Date,
        _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unsupported attribute blueprint data type.")
    };
}
