using EwaysVNext.Modules.Pricing.QueryApplication;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Pricing.Adapter.Inbound.GraphQL;

[ExtendObjectType(typeof(Query))]
public sealed class CurrencyGraphQLQueries
{
    /// <summary>
    /// Anonymous, like its REST twin: a storefront asks which currencies exist before a visitor
    /// has done anything, and requiring a token would make every page load wait on
    /// authentication it does not need.
    /// </summary>
    [AllowAnonymous]
    public Task<IReadOnlyList<CurrencyDto>> CurrenciesAsync(
        [Service] ICurrencyQueries queries, CancellationToken ct = default) =>
        queries.GetEnabledAsync(ct);
}
