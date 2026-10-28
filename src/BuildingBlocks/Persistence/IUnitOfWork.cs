namespace EwaysVNext.Persistence;

/// <summary>
/// What a save reports when it loses a race. Callers need the two apart: a stale write is worth
/// retrying, while a duplicate key is a business conflict — or, on an idempotent insert, a replay
/// of a request that already succeeded.
/// </summary>
public enum SaveOutcome
{
    Saved,
    UniqueViolation,
    ConcurrencyConflict,
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    int SaveChanges();

    /// <summary>
    /// Saves and reports a lost race instead of throwing. Anything that is not a concurrency
    /// conflict or a unique violation still propagates — this is not a catch-all.
    /// </summary>
    Task<SaveOutcome> TrySaveChangesAsync(CancellationToken ct = default);
}
