using EwaysVNext.Database;
using EwaysVNext.Modules.Partners.Application.Partners.Domain;
using EwaysVNext.Modules.Partners.Application.Usage;
using EwaysVNext.Outcomes;
using EwaysVNext.Queries.Application.Paginations;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Partners.QueryApplication;

public interface IPartnerAdminQueries
{
    Task<PaginatedQuery<PartnerListItem>> ListAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// <c>now</c> is passed in rather than read here, so a key's derived status is decided by the
    /// caller's one reading of the clock and every key in the response is judged against the same
    /// instant.
    /// </summary>
    Task<Result<PartnerDetail>> GetAsync(long partnerId, DateTimeOffset now, CancellationToken ct = default);

    Task<PaginatedQuery<ApiKeyUsageItem>> GetUsageAsync(
        long partnerId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct = default);
}

public sealed class PartnerAdminQueries(EwaysVNextDbContext db) : IPartnerAdminQueries
{
    public const int MaxPageSize = 100;

    public async Task<PaginatedQuery<PartnerListItem>> ListAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Clamp(page, pageSize);

        var query = db.Partners.AsNoTracking();
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(partner => partner.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(partner => new PartnerListItem(
                partner.Id,
                partner.Name,
                partner.Status.ToString(),
                partner.Scopes,
                partner.RateLimitPerMinute,
                partner.BulkMaxItems,
                partner.Keys.Count(key => key.RevokedAt == null),
                partner.CreatedAt))
            .ToListAsync(ct);

        return new PaginatedQuery<PartnerListItem>(page, pageSize, PageCount(total, pageSize), total, items);
    }

    public async Task<Result<PartnerDetail>> GetAsync(
        long partnerId, DateTimeOffset now, CancellationToken ct = default)
    {
        var partner = await db.Partners.AsNoTracking()
            .Where(p => p.Id == partnerId)
            .Select(p => new PartnerListItem(
                p.Id,
                p.Name,
                p.Status.ToString(),
                p.Scopes,
                p.RateLimitPerMinute,
                p.BulkMaxItems,
                p.Keys.Count(key => key.RevokedAt == null),
                p.CreatedAt))
            .FirstOrDefaultAsync(ct);

        if (partner is null)
            return Result.Failure<PartnerDetail>(Error.NotFound);

        var keys = await db.Set<ApiKey>().AsNoTracking()
            .Where(key => key.PartnerId == partnerId)
            .OrderByDescending(key => key.CreatedAt)
            .Select(key => new ApiKeyView(
                key.Id,
                key.Prefix,
                key.Scopes,
                key.CreatedAt,
                key.ExpiresAt,
                key.RevokedAt,
                key.RevokedAt != null
                    ? ApiKeyStatuses.Revoked
                    : key.ExpiresAt != null && key.ExpiresAt <= now
                        ? ApiKeyStatuses.Expired
                        : ApiKeyStatuses.Active,
                // Correlated so it stays one query. The (partner_id, at DESC) index does not
                // serve this; it is a small per-key lookup on a page of at most a few keys.
                db.ApiKeyUsages
                    .Where(usage => usage.KeyId == key.Id)
                    .Max(usage => (DateTimeOffset?)usage.At)))
            .ToListAsync(ct);

        return Result.Success(new PartnerDetail(partner, keys));
    }

    public async Task<PaginatedQuery<ApiKeyUsageItem>> GetUsageAsync(
        long partnerId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        (page, pageSize) = Clamp(page, pageSize);

        var query = db.ApiKeyUsages.AsNoTracking()
            .Where(usage => usage.PartnerId == partnerId);

        if (from is not null)
            query = query.Where(usage => usage.At >= from);

        if (to is not null)
            query = query.Where(usage => usage.At < to);

        var total = await query.CountAsync(ct);

        // Newest first, which is both what an admin wants and the direction the
        // (partner_id, at DESC) index already orders.
        var items = await query
            .OrderByDescending(usage => usage.At)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(usage => new ApiKeyUsageItem(
                usage.KeyId, usage.At, usage.Method, usage.Path,
                usage.Status, usage.CorrelationId, usage.DurationMs))
            .ToListAsync(ct);

        return new PaginatedQuery<ApiKeyUsageItem>(page, pageSize, PageCount(total, pageSize), total, items);
    }

    /// <summary>
    /// Clamps rather than refusing. A page size of 10,000 is a caller being optimistic, not an
    /// attack, and answering 100 rows is more useful than a 400 — but it must not be possible to
    /// ask for the whole table in one request.
    /// </summary>
    private static (int Page, int PageSize) Clamp(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));

    private static int PageCount(int total, int pageSize) =>
        total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
}
