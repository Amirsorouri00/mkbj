namespace EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;

/// <summary>
/// Tells the rest of the system that prices moved.
/// <para>
/// Two kinds, like the catalogue's. A <b>price</b> change bumps a version a cart can compare
/// against and asks for the storefront to be rebuilt. An <b>FX</b> change does both of those and
/// first rebuilds the resolved-rate table every converted price is read through — so the order
/// matters: announcing before the rebuild would send readers to the old rates.
/// </para>
/// <para>
/// Called after the caller's save has committed; failures are logged and swallowed, because the
/// nightly refresh is the backstop and no purchase path reads the derived tables.
/// </para>
/// </summary>
public interface IPricingChangeNotifier
{
    Task PricesChangedAsync(CancellationToken ct = default);

    Task FxChangedAsync(CancellationToken ct = default);

    Task<long> CurrentVersionAsync(CancellationToken ct = default);
}
