using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;

/// <summary>
/// How a source listing becomes a catalogue product.
/// <para>
/// Idempotent by construction: an import runs the same command again on every pass, so publishing
/// twice must leave the catalogue exactly as publishing once did. That is why the result says
/// what it <em>created</em> rather than just what succeeded — a caller cannot tell the difference
/// from the product alone.
/// </para>
/// </summary>
public interface ICatalogPublishingPort
{
    Task<Result<PublishedProduct>> UpsertProductAsync(
        PublishProductCommand command, CancellationToken ct = default);
}
