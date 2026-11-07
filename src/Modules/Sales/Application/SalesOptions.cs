namespace EwaysVNext.Modules.Sales.Application;

/// <summary>
/// Deployment-level limits: how much work a batch may do at once, and how long a candidate pool
/// may be reused.
/// </summary>
/// <remarks>
/// Configuration rather than settings data. These are about the machine the system runs on — how
/// many orders it can compose in parallel without starving everything else — not about what the
/// seller sells, which lives in Settings.
/// </remarks>
public sealed class SalesOptions
{
    public const string SectionName = "Sales";

    /// <summary>How many bulk items are composed at once.</summary>
    public int BulkParallelism { get; set; } = 4;

    /// <summary>How long a whole batch may run before the rest is handed back unprocessed.</summary>
    public int BulkDeadlineSeconds { get; set; } = 120;

    /// <summary>
    /// How long a candidate pool may be reused.
    /// </summary>
    /// <remarks>
    /// Short because it bounds nothing but convenience: a change to the catalogue or to prices
    /// invalidates the pool immediately through its versions, so this only limits how long an
    /// unchanged shop goes without a fresh read.
    /// </remarks>
    public int CandidateCacheSeconds { get; set; } = 30;
}
