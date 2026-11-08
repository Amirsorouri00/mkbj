using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Security;

/// <summary>
/// Every route is authorized unless it is on the list, and the list is short and argued.
/// </summary>
/// <remarks>
/// The failure this prevents is the quiet one: a new admin endpoint mapped without
/// <c>.RequireAuthorization(...)</c> works perfectly in every test anyone writes for it, because
/// those tests send a token. Nothing fails until somebody sends none. A rule that enumerates the
/// routing table is the only thing that notices.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class EndpointAuthorizationTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The routes that are anonymous on purpose.
    /// </summary>
    /// <remarks>
    /// The storefront is public because a shop that demands a token to show a product page is a
    /// shop nobody indexes and nobody shares a link to. The health and contract routes are read
    /// by load balancers and client generators that hold no credentials.
    /// </remarks>
    private static readonly string[] AnonymousByDesign =
    [
        "/api/v1/shop/home",
        "/api/v1/shop/categories",
        "/api/v1/shop/products",
        "/api/v1/shop/products/{slug}",
        "/api/v1/shop/facets",
        "/api/v1/shop/search",
        "/api/v1/shop/currencies",
        "/health",
        "/health/live",
        "/health/ready",
        "/graphql/{**slug}",
        "/openapi/{documentName}.json",
        "/scalar/{documentName?}",
    ];

    [Fact]
    public void Every_Route_Is_Authorized_Unless_Allowlisted()
    {
        using var factory = fixture.CreateFactory();

        var unprotected = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(endpoint => "/" + endpoint.RoutePattern.RawText!.TrimStart('/'))
            .Where(route => !AnonymousByDesign.Contains(route, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        unprotected.Should().BeEmpty(
            "a route with no authorization metadata is reachable by anyone; add "
            + ".RequireAuthorization(...) or argue it onto the allowlist");
    }

    /// <summary>
    /// The allowlist is checked against reality, not just trusted.
    /// </summary>
    /// <remarks>
    /// An entry that no longer matches a route is dead text, and dead text in a security
    /// allowlist is how a real exemption hides among stale ones.
    /// </remarks>
    [Fact]
    public void The_Anonymous_Allowlist_Has_No_Stale_Entries()
    {
        using var factory = fixture.CreateFactory();

        var routes = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => "/" + endpoint.RoutePattern.RawText!.TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        AnonymousByDesign.Where(entry => !routes.Contains(entry)).Should().BeEmpty();
    }

    /// <summary>
    /// An unboundedly nested query from an anonymous caller is refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What refuses this is HotChocolate's built-in coordinate-cycle guard — a default, not
    /// something this codebase configured.</b> That is worth stating plainly, because two
    /// mutation checks proved it: deleting <c>AddMaxExecutionDepthRule</c> and turning
    /// <c>EnforceCostLimits</c> off both leave this test passing. An earlier version asserted
    /// only that the word "depth" appeared in the message and passed with the depth rule
    /// deleted, because the built-in message happens to contain that word.
    /// </para>
    /// <para>
    /// The test still earns its place: it pins a default that an upgrade could change, and the
    /// exact message means it cannot drift back into passing for an unrelated reason.
    /// <c>GraphQLLimits.MaxDepth</c> and the cost ceiling are configured as defence in depth and
    /// bound non-cyclic nesting; nothing here exercises them, and this test does not pretend to.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_Deep_GraphQL_Query_Is_Refused()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        // Twelve levels of a genuinely self-referencing field: a few hundred bytes that ask for
        // an unbounded amount of work, from a caller holding no token at all.
        const int Levels = 12;

        var query = "query { shopCategories { id "
            + string.Concat(Enumerable.Repeat("children { id ", Levels))
            + string.Concat(Enumerable.Repeat("} ", Levels))
            + "} }";

        using var response = await client.PostAsJsonAsync("/graphql", new { query }, Ct);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        body.TryGetProperty("errors", out var errors).Should().BeTrue(
            "an unbounded query must be refused before it reaches a resolver");

        errors.EnumerateArray().Select(error => error.GetProperty("message").GetString())
            .Should().Contain("Maximum allowed coordinate cycle depth was exceeded.",
                "the exact message, so this cannot pass for an unrelated reason");
    }

    [Fact]
    public async Task A_Shallow_Storefront_Query_Still_Works()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/graphql", new { query = "query { shopCategories { id name } }" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        // The limit has to bite the pathological case without touching the ordinary one.
        body.TryGetProperty("errors", out _).Should().BeFalse();
    }
}
