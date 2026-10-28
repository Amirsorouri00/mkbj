namespace EwaysVNext.Jobs;

/// <summary>
/// Every job type in the system. Values equal their names so a row in <c>jobs.job</c> reads the
/// same as the code that enqueued it.
/// </summary>
public static class JobTypes
{
    public const string ImportJobStep = nameof(ImportJobStep);
    public const string FxRefresh = nameof(FxRefresh);
    public const string RefreshStorefrontViews = nameof(RefreshStorefrontViews);
    public const string AuditSeal = nameof(AuditSeal);
    public const string UsagePrune = nameof(UsagePrune);
    public const string StagingImageCleanup = nameof(StagingImageCleanup);
}
