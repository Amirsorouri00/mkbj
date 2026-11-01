using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;

namespace EwaysVNext.Modules.Pricing.Application.Fx.Domain;

/// <summary>
/// A rate an admin has fixed by hand, taking precedence over whatever the provider publishes.
/// <para>
/// One row per pair, and replacing an override mutates that row in place. The alternative —
/// deactivating the old row and inserting a new one — is two writes with a window between them
/// where the pair has no override at all, and a quote issued in that window would use the
/// provider's rate the admin was overriding.
/// </para>
/// <para>
/// The history is not lost: the audit trail records the before and after of every change.
/// </para>
/// </summary>
public sealed class FxRateOverride : AggregateRoot<long>
{
    public string Base { get; private set; } = null!;

    public string Quote { get; private set; } = null!;

    public decimal Rate { get; private set; }

    public Guid SetBy { get; private set; }

    public DateTimeOffset SetAt { get; private set; }

    /// <summary>
    /// When the override stops applying, exclusive. Null means it applies until an admin
    /// withdraws it.
    /// </summary>
    public DateTimeOffset? ValidUntil { get; private set; }

    public bool IsActive { get; private set; }

    private FxRateOverride() { }

    public static FxRateOverride Create(long id, string @base, string quote, decimal rate,
        Guid setBy, DateTimeOffset now, DateTimeOffset? validUntil)
    {
        if (id <= 0)
        {
            throw new DomainException("FX_OVERRIDE_ID_INVALID", "FX override ID must be positive.");
        }

        var normalizedBase = Currency.NormalizeCode(@base);
        var normalizedQuote = Currency.NormalizeCode(quote);
        FxRate.EnsureDifferentPair(normalizedBase, normalizedQuote);
        FxRate.EnsurePositiveRate(rate);
        EnsureFutureExpiry(now, validUntil);

        return new FxRateOverride
        {
            Id = id,
            Base = normalizedBase,
            Quote = normalizedQuote,
            Rate = rate,
            SetBy = setBy,
            SetAt = now,
            ValidUntil = validUntil,
            IsActive = true,
        };
    }

    /// <summary>
    /// Replaces the override in place, reactivating it if an admin had withdrawn it. Setting a
    /// rate <em>is</em> the decision to apply it; leaving the row inactive would be a second,
    /// silent step.
    /// </summary>
    public void Replace(decimal rate, Guid setBy, DateTimeOffset now, DateTimeOffset? validUntil)
    {
        FxRate.EnsurePositiveRate(rate);
        EnsureFutureExpiry(now, validUntil);

        Rate = rate;
        SetBy = setBy;
        SetAt = now;
        ValidUntil = validUntil;
        IsActive = true;
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive) return;

        IsActive = false;
        SetAt = now;
    }

    /// <summary>
    /// <c>ValidUntil</c> is exclusive: at the instant it names, the override has stopped. An
    /// inclusive bound would leave the rate applying for one tick beyond the time an admin typed.
    /// </summary>
    public bool IsEffectiveAt(DateTimeOffset now) =>
        IsActive && (ValidUntil is null || now < ValidUntil);

    private static void EnsureFutureExpiry(DateTimeOffset now, DateTimeOffset? validUntil)
    {
        if (validUntil is { } expiry && expiry <= now)
        {
            throw new DomainException(
                "FX_OVERRIDE_EXPIRY_INVALID",
                "An override that has already expired cannot be set; withdraw it instead.");
        }
    }
}
