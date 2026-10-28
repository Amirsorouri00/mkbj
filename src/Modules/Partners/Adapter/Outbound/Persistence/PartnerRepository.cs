using EwaysVNext.Database;
using EwaysVNext.Modules.Partners.Application.Partners.Domain;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Partners.Adapter.Outbound.Persistence;

/// <summary>
/// Loads the aggregate with its keys. Tracked, because every caller is about to change something —
/// the read-only authentication path uses <see cref="ApiKeyLookup"/> instead.
/// </summary>
public sealed class PartnerRepository(EwaysVNextDbContext db) : IPartnerRepository
{
    public Task<Partner?> GetAsync(long partnerId, CancellationToken ct = default) =>
        db.Partners
            .Include(partner => partner.Keys)
            .SingleOrDefaultAsync(partner => partner.Id == partnerId, ct);

    public void Add(Partner partner) => db.Partners.Add(partner);
}
