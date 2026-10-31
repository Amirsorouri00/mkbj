using EwaysVNext.Database;
using EwaysVNext.Modules.Settings.Application.Settings.Domain;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Settings.Adapter.Outbound.Persistence;

/// <summary>
/// Loads the singletons by their fixed id. Tracking is a parameter because the same rows serve
/// two very different callers: the read port, which must not track, and the admin service, which
/// must.
/// </summary>
public sealed class SettingsRepository(EwaysVNextDbContext db) : ISettingsRepository
{
    public Task<SellerProfile?> GetSellerAsync(bool tracking, CancellationToken ct = default) =>
        Track(db.SellerProfiles, tracking)
            .SingleOrDefaultAsync(profile => profile.Id == SettingsDefaults.SingletonId, ct);

    public Task<PaymentInstruction?> GetPaymentInstructionAsync(bool tracking, CancellationToken ct = default) =>
        Track(db.PaymentInstructions, tracking)
            .Include(instruction => instruction.Versions)
            .SingleOrDefaultAsync(instruction => instruction.Id == SettingsDefaults.SingletonId, ct);

    public Task<CommerceLimits?> GetCommerceLimitsAsync(bool tracking, CancellationToken ct = default) =>
        Track(db.CommerceLimits, tracking)
            .SingleOrDefaultAsync(limits => limits.Id == SettingsDefaults.SingletonId, ct);

    private static IQueryable<T> Track<T>(DbSet<T> set, bool tracking) where T : class =>
        tracking ? set : set.AsNoTracking();
}
