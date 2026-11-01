namespace EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;

/// <remarks><c>Provenance</c> is how the rate was arrived at, copied from the resolver's snapshot.</remarks>
public sealed record ResolvedRateRow(
    string Base,
    string Quote,
    decimal Rate,
    string Provenance,
    long? FxRateId,
    long? FxOverrideId,
    DateTimeOffset RateAt,
    DateTimeOffset? ValidUntil);

/// <summary>
/// The derived table the storefront's SQL joins against.
/// <para>
/// It exists so there is exactly one implementation of the resolution rules. Reimplementing
/// override-then-peg-then-cross in SQL would give two answers to the same question, and the one
/// a customer saw would depend on which path they came in through.
/// </para>
/// </summary>
public interface IResolvedRateProjection
{
    /// <summary>
    /// Replaces the whole table. Wholesale rather than row by row because the set is small — a
    /// handful of currencies squared — and a partial update would leave pairs from before an FX
    /// refresh sitting beside pairs from after it.
    /// </summary>
    Task ReplaceAllAsync(IReadOnlyList<ResolvedRateRow> rows, CancellationToken ct = default);
}
