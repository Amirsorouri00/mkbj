using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.IntegrationTests.Support;
using EwaysVNext.Modules.Partners.Application.Partners.Keys;
using EwaysVNext.Modules.Partners.QueryApplication;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Partners;

/// <summary>
/// The admin surface over HTTP, as a super admin. The single thread running through these is that
/// the plaintext key exists in exactly one response and can never be read back.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class PartnerAdminApiTests(PostgresFixture fixture)
{
    private const string Base = "/api/v1/admin/partners";

    private static readonly string OrdersCreateProbe =
        $"/test/partner/{PartnerProbeEndpoints.Slug(PartnerScopes.OrdersCreate)}";

    private WebApplicationFactory<Program> CreateFactory() =>
        fixture.CreateFactory(configureServices: services =>
            services.AddScoped<IEndpoint, PartnerProbeEndpoints>());

    private static HttpClient AdminClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateSuperAdminToken());

        return client;
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Every success body is wrapped as { message, data }.</summary>
    private static JsonElement Data(JsonElement body) => body.GetProperty("data");

    private static async Task<long> CreatePartner(HttpClient client, params string[] scopes)
    {
        using var response = await client.PostAsJsonAsync(Base, new
        {
            name = $"Acme {Guid.NewGuid():N}",
            scopes = scopes.Length == 0 ? new[] { PartnerScopes.OrdersCreate } : scopes,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await Body(response)).GetInt64();
    }

    private static async Task<(long KeyId, string Key, string Prefix)> IssueKey(
        HttpClient client, long partnerId)
    {
        using var response = await client.PostAsJsonAsync($"{Base}/{partnerId}/keys", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.Should().BeTrue(
            "the plaintext key is in this body and must not be cached on the way back");

        var data = Data(await Body(response));

        return (data.GetProperty("keyId").GetInt64(),
                data.GetProperty("plaintextKey").GetString()!,
                data.GetProperty("prefix").GetString()!);
    }

    [Fact]
    public async Task Super_Admin_Creates_Partner_And_Issues_A_Working_Key()
    {
        using var factory = CreateFactory();
        using var client = AdminClient(factory);

        var partnerId = await CreatePartner(client, PartnerScopes.OrdersCreate);
        var (_, key, _) = await IssueKey(client, partnerId);

        key.Should().MatchRegex("^pk_[0-9A-Za-z]{8}_[0-9A-Za-z]{43}$");

        using var probe = factory.CreateClient();
        using var response = await probe.GetWithBearerAsync(OrdersCreateProbe, key);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a key the admin surface just minted must work immediately");
    }

    [Fact]
    public async Task Plaintext_Key_Is_Returned_Once_And_Never_By_Reads()
    {
        using var factory = CreateFactory();
        using var client = AdminClient(factory);

        var partnerId = await CreatePartner(client);
        var (_, key, prefix) = await IssueKey(client, partnerId);
        var secret = key.Split('_')[2];

        using var detail = await client.GetAsync($"{Base}/{partnerId}");
        var restJson = await detail.Content.ReadAsStringAsync();

        restJson.Should().Contain(prefix, "the prefix is a lookup handle, not a secret");
        restJson.Should().NotContain(secret).And.NotContain(key);

        var graphJson = await GraphQl(client,
            $$"""{ adminPartner(id: {{partnerId}}) { keys { prefix status } } }""");

        graphJson.Should().Contain(prefix);
        graphJson.Should().NotContain(secret).And.NotContain(key,
            "there is nothing stored that could reconstruct the key, and no read may return it");
    }

    [Fact]
    public async Task Partner_Detail_Shows_Rotation_And_Revocation_State()
    {
        using var factory = CreateFactory();
        using var client = AdminClient(factory);

        var partnerId = await CreatePartner(client);
        var (keyId, _, _) = await IssueKey(client, partnerId);

        using var rotated = await client.PostAsJsonAsync(
            $"{Base}/{partnerId}/keys/{keyId}/rotate", new { overlapHours = 1.0 });
        rotated.StatusCode.Should().Be(HttpStatusCode.Created);

        var newKeyId = Data(await Body(rotated)).GetProperty("keyId").GetInt64();

        var keys = await Keys(client, partnerId);

        keys[keyId].GetProperty("status").GetString().Should().Be(ApiKeyStatuses.Active,
            "the rotated key keeps working through its overlap");
        keys[keyId].GetProperty("expiresAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        keys[newKeyId].GetProperty("status").GetString().Should().Be(ApiKeyStatuses.Active);
        keys[newKeyId].GetProperty("expiresAt").ValueKind.Should().Be(JsonValueKind.Null);

        using var revoke = await client.PostAsync($"{Base}/{partnerId}/keys/{keyId}/revoke", null);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        (await Keys(client, partnerId))[keyId].GetProperty("status").GetString()
            .Should().Be(ApiKeyStatuses.Revoked);
    }

    [Fact]
    public async Task Usage_Lists_Recorded_Requests_Newest_First()
    {
        using var factory = CreateFactory();
        using var client = AdminClient(factory);

        var partnerId = await CreatePartner(client, PartnerScopes.OrdersCreate);
        var (_, key, _) = await IssueKey(client, partnerId);

        using var probe = factory.CreateClient();
        await probe.GetWithBearerAsync(OrdersCreateProbe, key);
        await probe.GetWithBearerAsync(
            $"/test/partner/{PartnerProbeEndpoints.Slug(PartnerScopes.CustomersRead)}", key);

        var items = await WaitForUsage(client, partnerId, expected: 2);

        items.Should().HaveCount(2);

        var timestamps = items.Select(item => item.GetProperty("at").GetDateTimeOffset()).ToList();
        timestamps.Should().BeInDescendingOrder("an admin reads the most recent calls first");
    }

    [Fact]
    public async Task GraphQL_IssuePartnerApiKey_Returns_The_Key_Once()
    {
        using var factory = CreateFactory();
        using var client = AdminClient(factory);

        var partnerId = await CreatePartner(client, PartnerScopes.OrdersCreate);

        var json = await GraphQl(client,
            $$"""mutation { issuePartnerApiKey(id: {{partnerId}}) { keyId prefix key scopes } }""");

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("errors", out var errors).Should().BeFalse(
            $"the mutation should succeed, but returned: {errors}");

        var payload = document.RootElement.GetProperty("data").GetProperty("issuePartnerApiKey");

        var key = payload.GetProperty("key").GetString()!;
        key.Should().MatchRegex("^pk_[0-9A-Za-z]{8}_[0-9A-Za-z]{43}$");

        using var probe = factory.CreateClient();
        (await probe.GetWithBearerAsync(OrdersCreateProbe, key)).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_Partner_Returns_404()
    {
        using var factory = CreateFactory();
        using var client = AdminClient(factory);

        using var response = await client.GetAsync($"{Base}/999999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Body(response)).GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Staff_Cannot_Reach_The_Partner_Surface_Over_GraphQL()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateStaffToken());

        var json = await GraphQl(client, """{ adminPartners { totalCount } }""");

        json.Should().Contain("AUTH_NOT_AUTHORIZED",
            "the same data over a different protocol is not a different permission question");
    }

    [Fact]
    public async Task Staff_Cannot_Reach_The_Partner_Surface()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateStaffToken());

        using var response = await client.GetAsync(Base);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "issuing a key is handing out the ability to order in the seller's name");
    }

    private static async Task<Dictionary<long, JsonElement>> Keys(HttpClient client, long partnerId)
    {
        using var response = await client.GetAsync($"{Base}/{partnerId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Data(await Body(response)).GetProperty("keys").EnumerateArray()
            .ToDictionary(key => key.GetProperty("id").GetInt64(), key => key.Clone());
    }

    private static async Task<List<JsonElement>> WaitForUsage(
        HttpClient client, long partnerId, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            using var response = await client.GetAsync($"{Base}/{partnerId}/usage");
            var items = Data(await Body(response)).GetProperty("items")
                .EnumerateArray().Select(item => item.Clone()).ToList();

            if (items.Count >= expected)
                return items;

            await Task.Delay(100);
        }

        return [];
    }

    private static async Task<string> GraphQl(HttpClient client, string query)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/graphql", content);

        return await response.Content.ReadAsStringAsync();
    }
}
