namespace EwaysVNext.Modules.Catalog.Application.Storefront.Ports.Inbound;

/// <summary>
/// Asks for the storefront's materialized views to be rebuilt.
/// <para>
/// Deliberately a request, not a rebuild: refreshing a materialized view takes seconds, and an
/// admin saving a product must not wait for it. The views are a reading surface only — no
/// purchase path reads them — so being a few seconds behind is the correct trade.
/// </para>
/// </summary>
public interface IStorefrontRefreshTrigger
{
    Task RequestRefreshAsync(CancellationToken ct = default);
}

/// <summary>
/// The refresh job carries nothing: there is one set of views and the job rebuilds all of them.
/// It exists so the queue's generic payload has a type to name.
/// </summary>
public sealed record RefreshStorefrontViewsPayload();
