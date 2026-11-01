using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Core.Application.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EwaysVNext.IntegrationTests.Identity;

/// <summary>
/// PRD acceptance 13: staff run the shop; a super admin alone touches partners, settings, money
/// and other admins' authority.
/// <para>
/// Written against the HTTP surface rather than the permission matrix, because the matrix being
/// right is not the same as every route consulting it. Both gates — the realm policy and the
/// permission — have to actually be on the endpoint, and only a request proves that.
/// </para>
/// <para>
/// Later phases extend this class: FX and currency rows arrive with Pricing, refunds with Sales.
/// Those routes do not exist yet, so here they are covered only by the matrix unit test.
/// </para>
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class Acceptance13StaffPermissionTests(PostgresFixture fixture)
{
    /// <summary>Route, and a body that would be valid if the caller were allowed.</summary>
    public static TheoryData<string, string, string?> SuperAdminOnlyRoutes => new()
    {
        { "GET", "/api/v1/admin/partners", null },
        { "POST", "/api/v1/admin/partners", """{"name":"Probe","scopes":["orders:read"]}""" },
        { "PUT", "/api/v1/admin/settings/seller",
            """{"legalName":"Probe","uen":"202012345A","address":"1 Raffles Place","email":"p@x.com","gstRegistered":false,"invoiceFooter":"","requiredBuyerFields":["email|phone"]}""" },
        { "POST", "/api/v1/admin/settings/payment-instructions", """{"text":"Probe instruction"}""" },
        { "PUT", "/api/v1/admin/settings/commerce-limits",
            """{"minUsd":"10.00","maxUsd":"20000.00","bulkDefaultMax":10,"compositionDeadlineMs":5000,"compositionMaxLines":50}""" },
    };

    private static HttpClient Client(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string method, string route, string? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), route);

        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        return client.SendAsync(request);
    }

    [Theory]
    [MemberData(nameof(SuperAdminOnlyRoutes))]
    public async Task Staff_Is_Forbidden(string method, string route, string? body)
    {
        using var factory = fixture.CreateFactory();
        using var client = Client(factory, AdminJwtFactory.CreateStaffToken());

        using var response = await Send(client, method, route, body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{method} {route} is super-admin work");
    }

    [Theory]
    [MemberData(nameof(SuperAdminOnlyRoutes))]
    public async Task Super_Admin_Is_Allowed_On_The_Same_Routes(string method, string route, string? body)
    {
        using var factory = fixture.CreateFactory();
        using var client = Client(factory, AdminJwtFactory.CreateSuperAdminToken());

        using var response = await Send(client, method, route, body);

        // The positive control. Without it, a route that is broken for everyone would pass the
        // refusal test and look like correct security.
        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.OK, HttpStatusCode.Created],
            $"{method} {route} must work for the role that owns it");
    }

    [Fact]
    public async Task Staff_Cannot_Touch_A_Partners_Keys()
    {
        using var factory = fixture.CreateFactory();

        using var super = Client(factory, AdminJwtFactory.CreateSuperAdminToken());

        using var created = await super.PostAsJsonAsync("/api/v1/admin/partners", new
        {
            name = $"Acme {Guid.NewGuid():N}",
            scopes = new[] { PartnerScopes.OrdersRead },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var partnerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetInt64();

        using var issued = await super.PostAsJsonAsync($"/api/v1/admin/partners/{partnerId}/keys", new { });
        var keyId = (await issued.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("keyId").GetInt64();

        using var staff = Client(factory, AdminJwtFactory.CreateStaffToken());

        foreach (var route in new[]
                 {
                     $"/api/v1/admin/partners/{partnerId}/keys",
                     $"/api/v1/admin/partners/{partnerId}/keys/{keyId}/rotate",
                     $"/api/v1/admin/partners/{partnerId}/keys/{keyId}/revoke",
                     $"/api/v1/admin/partners/{partnerId}/suspend",
                 })
        {
            using var response = await Send(staff, "POST", route, "{}");

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
                $"{route} hands out or withdraws the ability to order in the seller's name");
        }

        // And nothing moved: the key is still live and the partner still active.
        using var detail = await super.GetAsync($"/api/v1/admin/partners/{partnerId}");
        var data = (await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

        data.GetProperty("partner").GetProperty("status").GetString().Should().Be("Active");
        data.GetProperty("keys").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("status").GetString().Should().Be("Active");
    }

    [Theory]
    [InlineData("""mutation { createPartner(name: "x", scopes: ["orders:read"]) }""")]
    [InlineData("""mutation { updateCommerceLimits(minUsd: "1.00", maxUsd: "2.00", bulkDefaultMax: 1, compositionDeadlineMs: 1000, compositionMaxLines: 1) }""")]
    [InlineData("""mutation { createPermission(input: { code: "x", name: "x", rules: [] }) { permission { id } } }""")]
    public async Task Staff_Is_Forbidden_On_GraphQL(string query)
    {
        using var factory = fixture.CreateFactory();
        using var client = Client(factory, AdminJwtFactory.CreateStaffToken());

        using var content = new StringContent(
            JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/graphql", content);

        (await response.Content.ReadAsStringAsync()).Should().Contain("AUTH_NOT_AUTHORIZED",
            "the same operation over a different protocol is not a different permission question");
    }

    [Fact]
    public async Task Staff_Can_Manage_Customers()
    {
        using var factory = fixture.CreateFactory();
        using var client = Client(factory, AdminJwtFactory.CreateStaffToken());

        using var list = await client.GetAsync("/api/v1/admin/customers");

        list.StatusCode.Should().Be(HttpStatusCode.OK,
            "running the shop is what staff are for; a split that stopped them would be useless");
    }

    [Fact]
    public async Task A_Shop_Token_Claiming_super_admin_Is_Still_Forbidden()
    {
        using var factory = fixture.CreateFactory();

        using var client = Client(factory,
            AdminJwtFactory.CreateToken(AdminRoles.ShopRealm, AdminRoles.SuperAdmin));

        using var response = await Send(client, "POST", "/api/v1/admin/partners",
            """{"name":"Forged","scopes":["orders:read"]}""");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the Shop realm is open self-service registration, so a role named there is not "
            + "backoffice authority however it is spelled");
    }
}
