namespace EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;

/// <remarks>
/// <c>Exists</c> means the variant is there at all, regardless of status. Pricing needs that
/// before writing a price: a price for a variant that does not exist is unreachable data, while a
/// price for a draft variant is perfectly normal — it is how a product is made ready before it
/// goes live.
/// </remarks>
public sealed record VariantState(long VariantId, long ProductId, bool Exists, bool IsSellable);

/// <summary>
/// Answers "does this variant exist, and is it sellable" for ids a caller already holds — the
/// cheap existence check, as opposed to <see cref="ISellableVariantPort"/>'s full projection.
/// </summary>
public interface IVariantLookupPort
{
    Task<IReadOnlyDictionary<long, VariantState>> GetStatesAsync(
        IReadOnlyCollection<long> variantIds, CancellationToken ct = default);
}
