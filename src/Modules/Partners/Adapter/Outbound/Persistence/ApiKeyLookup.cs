using EwaysVNext.Database;
using EwaysVNext.Modules.Partners.Application.Partners.Domain;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Partners.Adapter.Outbound.Persistence;

/// <summary>
/// The authentication read: one indexed lookup on the clear prefix, projected without tracking.
/// <para>
/// It runs on every partner request, so it deliberately does not load the aggregate — that would
/// track a graph for a change that never comes and pull back every key the partner holds to use
/// one of them.
/// </para>
/// </summary>
public sealed class ApiKeyLookup(EwaysVNextDbContext db) : IApiKeyLookup
{
    public async Task<ApiKeyCredential?> FindByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var row = await db.Set<ApiKey>()
            .AsNoTracking()
            .Where(key => key.Prefix == prefix)
            .Join(
                db.Partners.AsNoTracking(),
                key => key.PartnerId,
                partner => partner.Id,
                (key, partner) => new
                {
                    key.Id,
                    key.PartnerId,
                    key.SecretHash,
                    KeyScopes = key.Scopes,
                    key.ExpiresAt,
                    key.RevokedAt,
                    partner.Status,
                    partner.RateLimitPerMinute,
                    partner.BulkMaxItems,
                })
            .SingleOrDefaultAsync(ct);

        return row is null
            ? null
            : new ApiKeyCredential(
                row.Id,
                row.PartnerId,
                row.SecretHash,
                row.KeyScopes,
                row.ExpiresAt,
                row.RevokedAt,
                PartnerActive: row.Status == PartnerStatus.Active,
                row.RateLimitPerMinute,
                row.BulkMaxItems);
    }
}
