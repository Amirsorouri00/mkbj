namespace EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;

/// <summary>
/// Whether a variant exists at all — regardless of whether it is sellable.
/// <para>
/// Pricing a draft variant is normal: it is how a product is made ready before it goes live. A
/// price for a variant that does not exist is unreachable data, which is the only case worth
/// refusing.
/// </para>
/// </summary>
public interface ICatalogVariantGateway
{
    Task<bool> VariantExistsAsync(long variantId, CancellationToken ct = default);
}
