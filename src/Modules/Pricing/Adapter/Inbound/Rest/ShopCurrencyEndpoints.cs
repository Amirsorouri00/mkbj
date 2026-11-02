using EwaysVNext.Modules.Pricing.QueryApplication;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Pricing.Adapter.Inbound.Rest;

/// <summary>
/// The currency picker a storefront renders before anyone has signed in.
/// </summary>
public sealed class ShopCurrencyEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Anonymous on purpose: this is the first thing a storefront asks for, before a visitor
        // has done anything, and requiring a token to learn which currencies exist would make
        // every page load wait on authentication it does not need.
        var group = app.MapGroup("/api/v1/shop").WithTags("Shop");

        group.MapGet("/currencies", async (ICurrencyQueries queries, CancellationToken ct) =>
                Result.Success(await queries.GetEnabledAsync(ct)).ToHttpResult())
            .AllowAnonymous();
    }
}
