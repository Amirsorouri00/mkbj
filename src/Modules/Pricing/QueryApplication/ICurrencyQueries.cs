namespace EwaysVNext.Modules.Pricing.QueryApplication;

/// <summary>
/// The currencies a shopper may choose. Anonymous-readable, which is why it exposes only what a
/// storefront needs rather than the admin view of the same rows.
/// </summary>
public interface ICurrencyQueries
{
    Task<IReadOnlyList<CurrencyDto>> GetEnabledAsync(CancellationToken ct = default);

    Task<bool> IsEnabledAsync(string code, CancellationToken ct = default);
}
