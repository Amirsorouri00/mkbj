using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Customer.Application.Customers.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Customers;

/// <summary>
/// The shop's own profile surface. The thread running through these is that the customer record
/// is created on first sight rather than at registration — nothing in Keycloak tells this system
/// someone signed up.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class ShopProfileTests(PostgresFixture fixture)
{
    private const string Me = "/api/v1/shop/me";

    private static HttpClient ShopClient(
        WebApplicationFactory<Program> factory, Guid subject, string email, bool verified)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", AdminJwtFactory.CreateShopToken(subject, email, verified));

        return client;
    }

    private static JsonElement Data(JsonElement body) => body.GetProperty("data");

    private static async Task<JsonElement> GetProfile(HttpClient client)
    {
        using var response = await client.GetAsync(Me);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Data(await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    [Fact]
    public async Task First_Me_Call_Creates_The_Website_Customer_Lazily()
    {
        using var factory = fixture.CreateFactory();
        var subject = Guid.NewGuid();
        var email = $"c-{Guid.NewGuid():N}@x.com";

        using var client = ShopClient(factory, subject, email, verified: false);

        var first = await GetProfile(client);

        first.GetProperty("email").GetString().Should().Be(email);
        first.GetProperty("emailVerified").GetBoolean().Should().BeFalse();
        first.GetProperty("status").GetString().Should().Be(nameof(CustomerStatus.Active));

        var second = await GetProfile(client);

        second.GetProperty("id").GetInt64().Should().Be(first.GetProperty("id").GetInt64(),
            "the second call finds the record the first one created");
    }

    [Fact]
    public async Task Me_Mirrors_Email_Verification_From_The_Token()
    {
        using var factory = fixture.CreateFactory();
        var subject = Guid.NewGuid();
        var email = $"v-{Guid.NewGuid():N}@x.com";

        using (var unverified = ShopClient(factory, subject, email, verified: false))
        {
            (await GetProfile(unverified)).GetProperty("emailVerified").GetBoolean().Should().BeFalse();
        }

        using var verified = ShopClient(factory, subject, email, verified: true);

        (await GetProfile(verified)).GetProperty("emailVerified").GetBoolean().Should().BeTrue(
            "the identity provider is the authority for a website account's address");
    }

    [Fact]
    public async Task Update_Profile_Persists_And_Records_The_Phone_Change()
    {
        using var factory = fixture.CreateFactory();
        var subject = Guid.NewGuid();

        using var client = ShopClient(factory, subject, $"p-{Guid.NewGuid():N}@x.com", verified: true);

        using var response = await client.PutAsJsonAsync(Me, new
        {
            fullName = "Martin Tan",
            companyName = "Acme Pte Ltd",
            phone = "+65 9123 4567",
            billingAddress = new
            {
                line1 = "1 Raffles Place",
                line2 = (string?)null,
                city = "Singapore",
                postalCode = "048616",
                countryIso2 = "sg",
            },
            registrationNumber = "202012345A",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = Data(await response.Content.ReadFromJsonAsync<JsonElement>());

        profile.GetProperty("phone").GetString().Should().Be("+6591234567",
            "the customer types it however they like; storage is E.164");
        profile.GetProperty("fullName").GetString().Should().Be("Martin Tan");
        profile.GetProperty("billingAddress").GetProperty("countryIso2").GetString().Should().Be("SG");

        var customerId = profile.GetProperty("id").GetInt64();

        using var scope = factory.Services.CreateScope();
        var changes = await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
            .Customers.AsNoTracking()
            .Where(customer => customer.Id == customerId)
            .SelectMany(customer => customer.ContactChanges)
            .ToListAsync();

        changes.Should().Contain(change =>
            change.Field == ContactField.Phone
            && change.NewValue == "+6591234567"
            && change.SourcePartnerId == null,
            "the customer changed it themselves, so no partner is the source");
    }

    [Fact]
    public async Task Invalid_Phone_Is_400_Validation_Failed()
    {
        using var factory = fixture.CreateFactory();

        using var client = ShopClient(factory, Guid.NewGuid(), $"i-{Guid.NewGuid():N}@x.com", true);

        using var response = await client.PutAsJsonAsync(Me, new { phone = "91234567" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Backoffice_Token_Cannot_Call_Shop_Me()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateSuperAdminToken());

        using var response = await client.GetAsync(Me);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an admin token names no customer, so there is nobody for /me to be");
    }

    [Fact]
    public async Task GraphQL_Me_And_UpdateProfile_Match_Rest()
    {
        using var factory = fixture.CreateFactory();
        var subject = Guid.NewGuid();
        var email = $"g-{Guid.NewGuid():N}@x.com";

        using var client = ShopClient(factory, subject, email, verified: true);

        var created = await GetProfile(client);
        var customerId = created.GetProperty("id").GetInt64();

        var mutation = await GraphQl(client,
            """mutation { updateProfile(input: { fullName: "GraphQL Name", phone: "+6598765432" }) { id email fullName phone } }""");

        using var document = JsonDocument.Parse(mutation);
        document.RootElement.TryGetProperty("errors", out var errors).Should().BeFalse(
            $"the mutation should succeed, but returned: {errors}");

        var payload = document.RootElement.GetProperty("data").GetProperty("updateProfile");

        payload.GetProperty("id").GetInt64().Should().Be(customerId,
            "both protocols resolve the same caller to the same customer");
        payload.GetProperty("fullName").GetString().Should().Be("GraphQL Name");
        payload.GetProperty("phone").GetString().Should().Be("+6598765432");

        // And the REST read sees it, which is the point of the two sharing one service.
        (await GetProfile(client)).GetProperty("fullName").GetString().Should().Be("GraphQL Name");
    }

    private static async Task<string> GraphQl(HttpClient client, string query)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/graphql", content);

        return await response.Content.ReadAsStringAsync();
    }
}
