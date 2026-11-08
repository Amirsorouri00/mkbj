using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.IntegrationTests.Contracts;

/// <summary>
/// The shape of the two public contracts, recorded so a change has to be deliberate.
/// </summary>
/// <remarks>
/// <para>
/// A breaking change to an API is rarely written on purpose; it falls out of a rename, a DTO
/// tidy-up, or a route moved while refactoring. Nothing in a normal test run notices, because
/// every test here is updated in the same commit. A recorded surface is what makes the change
/// visible in review.
/// </para>
/// <para>
/// <b>Paths and field names, not the whole document.</b> Snapshotting every byte would fail on
/// a reordered property or a reworded description, and a test that cries wolf gets its snapshot
/// regenerated without being read — which is worse than not having it.
/// </para>
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class ContractSnapshotTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The routes a partner's code calls, which may not change or vanish without a version.
    /// </summary>
    /// <remarks>
    /// Partner routes only. The admin and shop surfaces are this team's own, served to clients
    /// it ships alongside; a partner's integration is somebody else's code that was written once
    /// and must keep working.
    /// </remarks>
    private static readonly string[] PartnerRoutes =
    [
        "/api/v1/customers/{id}/orders",
        "/api/v1/invoices/search",
        "/api/v1/invoices/{id}",
        "/api/v1/invoices/{id}/pdf",
        "/api/v1/orders",
        "/api/v1/orders/bulk",
        "/api/v1/orders/{id}",
    ];

    [Fact]
    public async Task No_Partner_Route_Disappears_Without_Being_Noticed()
    {
        var document = await DocumentAsync();
        var published = document["paths"]!.AsObject().Select(path => path.Key).ToList();

        // Missing is the breaking half: an added route breaks nobody, a removed one breaks
        // every integration that calls it.
        PartnerRoutes.Where(route => !published.Contains(route, StringComparer.Ordinal))
            .Should().BeEmpty(
                "a partner route that vanished breaks code this team did not write and cannot fix");
    }

    [Fact]
    public async Task The_Order_Response_Keeps_The_Fields_A_Partner_Reads()
    {
        var document = await DocumentAsync();

        var schema = document["paths"]!["/api/v1/orders/{id}"]!["get"]!["responses"]!["200"]
            ?["content"]?["application/json"]?["schema"];

        if (schema is null) return;

        var fields = FieldNames(schema, document);

        // The handful a partner's reconciliation actually depends on. Renaming any of them
        // silently empties a column in somebody else's report.
        fields.Should().Contain(["id", "number", "status", "currency"]);
    }

    [Fact]
    public async Task The_Graphql_Schema_Keeps_Its_Shop_And_Admin_Entry_Points()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        const string Query = """
            query { __schema { queryType { fields { name } } mutationType { fields { name } } } }
            """;

        var response = await AdminGraphQLClient.PostForResultAsync<JsonElement>(
            client, Query, null, Ct);

        response.Errors.Should().BeNull();

        var schema = response.Data!.GetProperty("__schema");

        var queries = Names(schema.GetProperty("queryType"));
        var mutations = Names(schema.GetProperty("mutationType"));

        // The storefront fields are what a shop front end is built on; the admin ones are what
        // the backoffice calls. Either disappearing is a broken page somewhere.
        queries.Should().Contain(["shopHome", "shopProducts", "cart", "myOrders"]);
        queries.Should().Contain(["adminOrders", "adminDashboard"]);

        mutations.Should().Contain(["addToCart", "checkout"]);
        mutations.Should().Contain(["confirmOrderPayment", "refundOrder"]);
    }

    [Fact]
    public async Task The_Storefront_Stays_Reachable_Without_A_Token()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/graphql", new { query = "query { shopCategories { id name } }" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        // A shop that demands a token to list its categories is a shop nobody indexes.
        body.TryGetProperty("errors", out _).Should().BeFalse();
    }

    private static List<string> Names(JsonElement type) =>
        [.. type.GetProperty("fields").EnumerateArray()
            .Select(field => field.GetProperty("name").GetString()!)];

    /// <summary>The property names of a schema, following one <c>$ref</c>.</summary>
    private static List<string> FieldNames(JsonNode schema, JsonNode document)
    {
        var resolved = schema;

        if (schema["$ref"]?.GetValue<string>() is { } reference)
        {
            var name = reference.Split('/').Last();
            resolved = document["components"]?["schemas"]?[name] ?? schema;
        }

        // The envelope wraps the payload in "data"; the fields a partner reads are inside it.
        if (resolved["properties"]?["data"] is { } data) return FieldNames(data, document);

        return resolved["properties"] is JsonObject properties
            ? [.. properties.Select(property => property.Key)]
            : [];
    }

    private async Task<JsonNode> DocumentAsync()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
    }
}
