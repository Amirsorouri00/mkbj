namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;

/// <summary>
/// Tells the rest of the system that the catalogue moved.
/// <para>
/// Two kinds of change, because they cost different things. A <b>sellability</b> change — a
/// product going active, a variant switching off, a price appearing — bumps a version number that
/// a cart can compare against to notice its contents went stale. A <b>display</b> change — a
/// retitled product, a new image — only needs the views rebuilt; bumping the version for it would
/// invalidate every cart in the system because someone fixed a typo.
/// </para>
/// <para>
/// Every method is called <b>after</b> the operation's save has committed, and a failure is
/// logged and swallowed. The nightly refresh is the backstop, and no purchase path reads the
/// views — so losing a notification delays a listing, it does not sell anything wrongly.
/// </para>
/// </summary>
public interface ICatalogChangeNotifier
{
    Task SellabilityChangedAsync(CancellationToken ct = default);

    Task DisplayChangedAsync(CancellationToken ct = default);

    Task<long> CurrentVersionAsync(CancellationToken ct = default);
}
