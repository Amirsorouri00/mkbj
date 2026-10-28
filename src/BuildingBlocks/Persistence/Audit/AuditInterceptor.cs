using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EwaysVNext.Persistence.Audit;

public sealed class AuditInterceptor(IUserProfileService userProfile, IClock clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;

        foreach (EntityEntry<IAuditable> entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.OccurredOn = clock.UtcNow;
                    entry.Entity.OccurredBy = userProfile.Email ?? "System";
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedAt = clock.UtcNow;
                    entry.Entity.ModifiedBy = userProfile.Email;
                    break;
            }
        }
    }
}
