using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Customers;

/// <summary>
/// The backoffice view of customers, as staff — these operations change no money and hand out no
/// access, so they are day-to-day shop work rather than super-admin work.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class CustomerAdminApiTests(PostgresFixture fixture)
{
    private const string Base = "/api/v1/admin/customers";

    private static HttpClient StaffClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateStaffToken());

        return client;
    }

    private static JsonElement Data(JsonElement body) => body.GetProperty("data");

    private static async Task<JsonElement> GetJson(HttpClient client, string path)
    {
        using var response = await client.GetAsync($"{Base}{path}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Data(await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static async Task<T> Scoped<T>(
        WebApplicationFactory<Program> factory, Func<IServiceProvider, Task<T>> use)
    {
        using var scope = factory.Services.CreateScope();

        return await use(scope.ServiceProvider);
    }

    private static async Task<long> SeedPartnerCustomer(
        WebApplicationFactory<Program> factory, long partnerId, string external, ContactInput contact)
    {
        var created = await Scoped(factory, services =>
            services.GetRequiredService<ICustomerResolutionPort>()
                .CreatePartnerCustomerAsync(partnerId, external, contact, "corr-seed"));

        created.IsSuccess.Should().BeTrue();

        return created.Value!.CustomerId;
    }

    [Fact]
    public async Task Staff_Lists_Partner_Linked_Customers_Filtered_By_Partner()
    {
        using var factory = fixture.CreateFactory();
        var partnerId = 8_100_001L;

        var mine = await SeedPartnerCustomer(
            factory, partnerId, $"ext-{Guid.NewGuid():N}", new ContactInput("mine@x.com", null, "Mine"));

        await SeedPartnerCustomer(
            factory, 8_100_002L, $"ext-{Guid.NewGuid():N}", new ContactInput("other@x.com", null, "Other"));

        using var client = StaffClient(factory);

        var page = await GetJson(client, $"/?kind=PartnerLinked&partnerId={partnerId}");

        var ids = page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64()).ToList();

        ids.Should().Contain(mine);
        page.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(item => item.GetProperty("partnerId").GetInt64() == partnerId);
    }

    [Fact]
    public async Task Detail_Shows_Contact_History_Newest_First_With_Source_And_Correlation()
    {
        using var factory = fixture.CreateFactory();
        var partnerId = 8_100_003L;
        var external = $"ext-{Guid.NewGuid():N}";

        var customerId = await SeedPartnerCustomer(
            factory, partnerId, external, new ContactInput("first@x.com", null, "First"));

        await Scoped(factory, services =>
            services.GetRequiredService<ICustomerResolutionPort>()
                .SyncPartnerContactAsync(customerId, partnerId, new ContactInput("second@x.com", null, null), "corr-a"));

        await Scoped(factory, services =>
            services.GetRequiredService<ICustomerResolutionPort>()
                .SyncPartnerContactAsync(customerId, partnerId, new ContactInput("third@x.com", null, null), "corr-b"));

        using var client = StaffClient(factory);

        var detail = await GetJson(client, $"/{customerId}");
        var history = detail.GetProperty("contactHistory").EnumerateArray().ToList();

        history.Should().HaveCount(2);

        history[0].GetProperty("newValue").GetString().Should().Be("third@x.com",
            "an admin reads the most recent change first");
        history[0].GetProperty("correlationId").GetString().Should().Be("corr-b");
        history[0].GetProperty("sourcePartnerId").GetInt64().Should().Be(partnerId);
        history[1].GetProperty("oldValue").GetString().Should().Be("first@x.com");
    }

    [Fact]
    public async Task Suspend_Blocks_The_Shop_Profile_Update()
    {
        using var factory = fixture.CreateFactory();
        var subject = Guid.NewGuid();

        var ensured = await Scoped(factory, services =>
            services.GetRequiredService<IWebsiteCustomerPort>()
                .EnsureFromIdentityAsync(subject, $"s-{Guid.NewGuid():N}@x.com", emailVerified: true));

        var customerId = ensured.Value!.CustomerId;

        using var staff = StaffClient(factory);

        using var suspended = await staff.PostAsync($"{Base}/{customerId}/suspend", null);
        suspended.StatusCode.Should().Be(HttpStatusCode.OK);

        using var shop = factory.CreateClient();
        shop.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", AdminJwtFactory.CreateShopToken(subject, "s@x.com", true));

        using var blocked = await shop.PutAsJsonAsync("/api/v1/shop/me", new { fullName = "Nope" });

        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("CUSTOMER_SUSPENDED");

        // And it is reversible: suspension stops the customer acting, not the seller's record.
        using var reactivated = await staff.PostAsync($"{Base}/{customerId}/reactivate", null);
        reactivated.StatusCode.Should().Be(HttpStatusCode.OK);

        using var allowed = await shop.PutAsJsonAsync("/api/v1/shop/me", new { fullName = "Yes" });
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_Matches_A_Fragment_Of_Phone_And_External_Id()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var external = $"ext-{suffix}";

        var customerId = await SeedPartnerCustomer(
            factory, 8_100_004L, external, new ContactInput(null, "+6591234567", "Searchable"));

        using var client = StaffClient(factory);

        var byExternal = await GetJson(client, $"/?search={suffix}");
        byExternal.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64())
            .Should().Contain(customerId, "an admin has a fragment, not a prefix");

        var byPhone = await GetJson(client, "/?search=9123456");
        byPhone.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64())
            .Should().Contain(customerId);
    }

    [Fact]
    public async Task GraphQL_AdminCustomer_Returns_Contact_History()
    {
        using var factory = fixture.CreateFactory();
        var partnerId = 8_100_005L;

        var customerId = await SeedPartnerCustomer(
            factory, partnerId, $"ext-{Guid.NewGuid():N}", new ContactInput("gql@x.com", null, "GraphQL"));

        await Scoped(factory, services =>
            services.GetRequiredService<ICustomerResolutionPort>()
                .SyncPartnerContactAsync(customerId, partnerId, new ContactInput("gql2@x.com", null, null), "corr-gql"));

        using var client = StaffClient(factory);

        var json = await GraphQl(client,
            $$"""{ adminCustomer(id: {{customerId}}) { customer { id status } contactHistory { field newValue correlationId } } }""");

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("errors", out var errors).Should().BeFalse(
            $"the query should succeed, but returned: {errors}");

        var detail = document.RootElement.GetProperty("data").GetProperty("adminCustomer");

        detail.GetProperty("customer").GetProperty("id").GetInt64().Should().Be(customerId);
        detail.GetProperty("contactHistory").EnumerateArray()
            .Should().ContainSingle(change =>
                change.GetProperty("correlationId").GetString() == "corr-gql");
    }

    [Fact]
    public async Task An_Unknown_Customer_Is_404()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);

        using var response = await client.GetAsync($"{Base}/987654321");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<string> GraphQl(HttpClient client, string query)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/graphql", content);

        return await response.Content.ReadAsStringAsync();
    }
}
