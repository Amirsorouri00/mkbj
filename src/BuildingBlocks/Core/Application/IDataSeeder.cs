namespace EwaysVNext.Core.Application;

/// <summary>
/// Seeds reference data a module needs to work at all — enabled currencies, the permission matrix,
/// seller defaults. Run by the host's <c>--seed</c> flag, which refuses to run in Production
/// (ADR-0017): demo data must never reach a real catalog, because a partner invoice composed from
/// fictitious SKUs is exactly what the PRD forbids.
/// </summary>
public interface IDataSeeder
{
    /// <summary>Lower runs first, so a seeder can depend on one with a smaller order.</summary>
    int Order { get; }

    Task SeedAsync(CancellationToken ct = default);
}
