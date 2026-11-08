using System.Net;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EwaysVNext.IntegrationTests.Contracts;

/// <summary>
/// The published partner contract.
/// </summary>
/// <remarks>
/// Somebody else's engineers generate clients from this document, often without being able to
/// ask a question. What they cannot see, they cannot handle — an undocumented 429 becomes an
/// unhandled exception in their nightly job — so these tests are about the document being
/// complete rather than merely present.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class PartnerContractTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Every partner route, and the refusals a caller has to handle.</summary>
    public static TheoryData<string, string> PartnerOperations => new()
    {
        { "post", "/api/v1/orders" },
        { "post", "/api/v1/orders/bulk" },
        { "get", "/api/v1/orders" },
        { "get", "/api/v1/orders/{id}" },
        { "get", "/api/v1/customers/{id}/orders" },
        { "get", "/api/v1/invoices/{id}" },
        { "get", "/api/v1/invoices/{id}/pdf" },
        { "post", "/api/v1/invoices/search" },
    };

    [Fact]
    public async Task The_Contract_Is_Published_Outside_Development()
    {
        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
        });

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", Ct);

        // A contract a partner cannot reach is not a contract.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_Contract_Can_Be_Withheld_By_Configuration()
    {
        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["OpenApi:Expose"] = "false",
        });

        using var client = factory.CreateClient();

        (await client.GetAsync("/openapi/v1.json", Ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound,
                "a deployment that serves no public API should be able to say so");
    }

    [Fact]
    public async Task The_Partner_Key_Scheme_Says_What_A_Key_Looks_Like()
    {
        var document = await DocumentAsync();

        var scheme = document["components"]!["securitySchemes"]!["PartnerApiKey"]!;

        scheme["type"]!.GetValue<string>().Should().Be("http");
        scheme["scheme"]!.GetValue<string>().Should().Be("bearer");

        // Spelled out because the format is not a JWT, and a partner reading "bearer" would
        // reasonably assume it was.
        scheme["bearerFormat"]!.GetValue<string>().Should().Contain("pk_");
    }

    [Theory]
    [MemberData(nameof(PartnerOperations))]
    public async Task A_Partner_Operation_Names_Its_Key_And_Its_Refusals(string method, string path)
    {
        var document = await DocumentAsync();

        var operation = document["paths"]?[path]?[method];
        operation.Should().NotBeNull($"{method.ToUpperInvariant()} {path} should be published");

        operation!["security"]![0]!["PartnerApiKey"].Should().NotBeNull(
            "a caller has to be told which credential this takes");

        var responses = operation["responses"]!.AsObject();

        foreach (var status in new[] { "401", "403", "429" })
        {
            responses.ContainsKey(status).Should().BeTrue(
                "{0} {1} must document {2}; what a client cannot see, it cannot handle",
                method, path, status);
        }
    }

    [Theory]
    [MemberData(nameof(PartnerOperations))]
    public async Task Every_Partner_Response_Carries_The_Correlation_Id(string method, string path)
    {
        var document = await DocumentAsync();
        var responses = document["paths"]![path]![method]!["responses"]!.AsObject();

        foreach (var (status, response) in responses)
        {
            // Most useful on the call that seemed to work: it is what a partner quotes in a
            // support request about an order they cannot find.
            response!["headers"]?["X-Correlation-Id"].Should().NotBeNull(
                "{0} {1} response {2}", method, path, status);
        }
    }

    [Fact]
    public async Task An_Anonymous_Storefront_Route_Takes_No_Partner_Key()
    {
        var document = await DocumentAsync();

        var operation = document["paths"]?["/api/v1/shop/categories"]?["get"];

        if (operation is null) return;

        var security = operation["security"];

        // The storefront is public on purpose; requiring a key in the document would send
        // integrators looking for one.
        if (security is JsonArray array && array.Count > 0)
        {
            array.Any(requirement => requirement?["PartnerApiKey"] is not null)
                .Should().BeFalse();
        }
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
