using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Settings.Application.Settings.Domain;

/// <summary>
/// The bounds the commerce flows run inside. A singleton.
/// </summary>
public sealed class CommerceLimits : AggregateRoot<long>
{
    private CommerceLimits()
    {
    }

    /// <summary>Smallest partner order amount, in USD minor units.</summary>
    public long MinUsdMinor { get; private set; }

    public long MaxUsdMinor { get; private set; }

    public int BulkDefaultMax { get; private set; }

    /// <summary>How long the composition engine may search before giving up.</summary>
    public int CompositionDeadlineMs { get; private set; }

    public int CompositionMaxLines { get; private set; }

    public static CommerceLimits CreateDefault()
    {
        var limits = new CommerceLimits { Id = SettingsDefaults.SingletonId };

        limits.Update(
            SettingsDefaults.MinUsdMinor,
            SettingsDefaults.MaxUsdMinor,
            SettingsDefaults.BulkDefaultMax,
            SettingsDefaults.CompositionDeadlineMs,
            SettingsDefaults.CompositionMaxLines);

        return limits;
    }

    public void Update(
        long minUsdMinor, long maxUsdMinor, int bulkDefaultMax, int compositionDeadlineMs, int compositionMaxLines)
    {
        if (minUsdMinor <= 0)
            throw new DomainException("VALIDATION_FAILED", "The minimum order amount must be positive.");

        if (maxUsdMinor <= minUsdMinor)
        {
            throw new DomainException("VALIDATION_FAILED",
                "The maximum order amount must be greater than the minimum.");
        }

        if (bulkDefaultMax is < 1 or > 1000)
            throw new DomainException("VALIDATION_FAILED", "The default bulk maximum must be between 1 and 1000.");

        // Capped below ten seconds so the composition engine always returns inside the published
        // p95: a deadline at or past the budget means the deadline never saves the request.
        if (compositionDeadlineMs is < 1000 or > 9500)
        {
            throw new DomainException("VALIDATION_FAILED",
                "The composition deadline must be between 1000 and 9500 ms, inside the 10 s budget.");
        }

        if (compositionMaxLines is < 1 or > 1000)
            throw new DomainException("VALIDATION_FAILED", "The composition line limit must be between 1 and 1000.");

        MinUsdMinor = minUsdMinor;
        MaxUsdMinor = maxUsdMinor;
        BulkDefaultMax = bulkDefaultMax;
        CompositionDeadlineMs = compositionDeadlineMs;
        CompositionMaxLines = compositionMaxLines;
    }
}
