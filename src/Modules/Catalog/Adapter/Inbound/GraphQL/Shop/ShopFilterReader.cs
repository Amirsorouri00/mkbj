using EwaysVNext.Modules.Catalog.QueryApplication.Storefront;
using Error = EwaysVNext.Outcomes.Error;
using Result = EwaysVNext.Outcomes.Result;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Money;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Shop;

/// <summary>
/// Turns a GraphQL filter input into the one <see cref="ProductListQuery"/> both surfaces use.
/// </summary>
/// <remarks>
/// Prices arrive as two-decimal strings here for the same reason they do over REST: GraphQL has
/// no decimal scalar, and a <c>Float</c> is a double, which does not hold money. Parsing them in
/// one place is what keeps the two surfaces answering identically.
/// </remarks>
internal static class ShopFilterReader
{
    public static EwaysVNext.Outcomes.Result<ProductListQuery> Read(
        string currency, ShopProductFilterInput? filter, StorefrontSort? sort,
        string? after, int? first)
    {
        var min = ReadMinor(filter?.PriceMin, "priceMin");
        if (min.IsFailure) return Result.Failure<ProductListQuery>(min.Error!);

        var max = ReadMinor(filter?.PriceMax, "priceMax");
        if (max.IsFailure) return Result.Failure<ProductListQuery>(max.Error!);

        var attributes = filter?.Attributes?
            .Where(attribute => attribute.Values is { Count: > 0 })
            .ToDictionary(
                attribute => attribute.Key,
                attribute => (IReadOnlyList<string>)[.. attribute.Values],
                StringComparer.Ordinal);

        return Result.Success(new ProductListQuery(
            currency,
            filter?.CategoryId,
            filter?.BrandIds is { Count: > 0 } brands ? [.. brands] : null,
            attributes is { Count: > 0 } ? attributes : null,
            min.Value,
            max.Value,
            filter?.Q,
            sort,
            after,
            first));
    }

    public static Exception AsGraphQLError(Error error) => error.ToGraphQLException();

    private static EwaysVNext.Outcomes.Result<long?> ReadMinor(string? text, string name)
    {
        if (string.IsNullOrEmpty(text)) return Result.Success<long?>(null);

        return MoneyText.TryParseMinor(text, out var minor)
            ? Result.Success<long?>(minor)
            : Result.Failure<long?>(new Error(
                "VALIDATION_FAILED",
                $"'{text}' is not a {name}. Use exactly two decimal places, for example \"10.00\".",
                400));
    }
}
