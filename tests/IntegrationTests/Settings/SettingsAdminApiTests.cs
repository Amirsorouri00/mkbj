using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Modules.Settings.Application.Settings.Domain;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Settings;

/// <summary>
/// The settings admin surface over HTTP. These run against the seeded singletons, so each test
/// leaves the values it changed — they assert on what they wrote rather than on the seed.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class SettingsAdminApiTests(PostgresFixture fixture)
{
    private const string Base = "/api/v1/admin/settings";

    private static readonly string[] ContactOnly = ["email|phone"];
    private static readonly string[] ContactAndCompany = ["email|phone", "companyName"];

    private static HttpClient AdminClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateSuperAdminToken());

        return client;
    }

    private static JsonElement Data(JsonElement body) => body.GetProperty("data");

    private static async Task<JsonElement> GetJson(HttpClient client, string path)
    {
        using var response = await client.GetAsync($"{Base}{path}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Data(await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static async Task<T> Resolve<T>(WebApplicationFactory<Program> factory, Func<ISettingsPort, Task<T>> read)
    {
        using var scope = factory.Services.CreateScope();

        return await read(scope.ServiceProvider.GetRequiredService<ISettingsPort>());
    }

    [Fact]
    public async Task Published_Instruction_Becomes_The_Active_Snapshot()
    {
        using var factory = fixture.CreateFactory();
        using var client = AdminClient(factory);

        var text = $"Wire to DBS {Guid.NewGuid():N}";

        using var response = await client.PostAsJsonAsync($"{Base}/payment-instructions", new { text });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var version = Data(await response.Content.ReadFromJsonAsync<JsonElement>()).GetInt32();

        var active = await Resolve(factory, port => port.GetActivePaymentInstructionAsync());

        active.Version.Should().Be(version);
        active.Text.Should().Be(text);
    }

    [Fact]
    public async Task Reactivating_An_Old_Version_Restores_Its_Text_Unchanged()
    {
        using var factory = fixture.CreateFactory();
        using var client = AdminClient(factory);

        var original = await Resolve(factory, port => port.GetActivePaymentInstructionAsync());

        using var published = await client.PostAsJsonAsync(
            $"{Base}/payment-instructions", new { text = $"Superseded {Guid.NewGuid():N}" });
        published.StatusCode.Should().Be(HttpStatusCode.Created);

        using var activated = await client.PostAsync(
            $"{Base}/payment-instructions/{original.Version}/activate", null);
        activated.StatusCode.Should().Be(HttpStatusCode.OK);

        var restored = await Resolve(factory, port => port.GetActivePaymentInstructionAsync());

        restored.Version.Should().Be(original.Version);
        restored.Text.Should().Be(original.Text,
            "a published version is immutable, so going back to it gives exactly what it said");
    }

    [Fact]
    public async Task Commerce_Limits_Round_Trip_As_Two_Decimal_Strings()
    {
        using var factory = fixture.CreateFactory();
        using var client = AdminClient(factory);

        using var update = await client.PutAsJsonAsync($"{Base}/commerce-limits", new
        {
            minUsd = "25.00",
            maxUsd = "900000.00",
            bulkDefaultMax = 50,
            compositionDeadlineMs = 7000,
            compositionMaxLines = 150,
        });

        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var view = await GetJson(client, "/commerce-limits");
        view.GetProperty("minUsd").GetString().Should().Be("25.00");
        view.GetProperty("maxUsd").GetString().Should().Be("900000.00");

        var limits = await Resolve(factory, port => port.GetCommerceLimitsAsync());
        limits.MinUsdMinor.Should().Be(2500, "the wire format is decimal, the storage is minor units");
        limits.CompositionDeadlineMs.Should().Be(7000);
    }

    [Theory]
    [InlineData("25")]
    [InlineData("25.0")]
    [InlineData("25.001")]
    [InlineData("-1.00")]
    [InlineData("")]
    [InlineData("abc")]
    public async Task Malformed_Usd_Amount_Is_400_Validation_Failed(string minUsd)
    {
        using var factory = fixture.CreateFactory();
        using var client = AdminClient(factory);

        using var response = await client.PutAsJsonAsync($"{Base}/commerce-limits", new
        {
            minUsd,
            maxUsd = "900000.00",
            bulkDefaultMax = 50,
            compositionDeadlineMs = 7000,
            compositionMaxLines = 150,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the API must not decide what a half-written amount stands for");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Gst_Registered_True_Is_Refused()
    {
        using var factory = fixture.CreateFactory();
        using var client = AdminClient(factory);

        using var response = await client.PutAsJsonAsync($"{Base}/seller", new
        {
            legalName = "Acme Pte Ltd",
            uen = "202012345A",
            address = "1 Raffles Place",
            email = "billing@acme.sg",
            gstRegistered = true,
            invoiceFooter = "",
            requiredBuyerFields = ContactOnly,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Seller_Update_Round_Trips()
    {
        using var factory = fixture.CreateFactory();
        using var client = AdminClient(factory);

        var legalName = $"Acme {Guid.NewGuid():N}";

        using var update = await client.PutAsJsonAsync($"{Base}/seller", new
        {
            legalName,
            uen = "202012345A",
            address = "1 Raffles Place",
            email = "billing@acme.sg",
            gstRegistered = false,
            invoiceFooter = "Thank you",
            requiredBuyerFields = ContactAndCompany,
        });

        update.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            var view = await GetJson(client, "/seller");
            view.GetProperty("legalName").GetString().Should().Be(legalName);
            view.GetProperty("requiredBuyerFields").EnumerateArray()
                .Select(field => field.GetString()).Should().Equal("email|phone", "companyName");
        }
        finally
        {
            // The seller is a singleton in a database this whole suite shares, and
            // requiredBuyerFields decides whether an invoice may be issued at all. Leaving
            // "companyName" behind made every partner order placed afterwards fail with
            // INVOICE_BUYER_FIELDS_MISSING — in the full run only, which is the worst way to
            // find out.
            using var restore = await client.PutAsJsonAsync($"{Base}/seller", new
            {
                legalName = SettingsDefaults.LegalName,
                uen = SettingsDefaults.Uen,
                address = SettingsDefaults.Address,
                email = SettingsDefaults.Email,
                gstRegistered = false,
                invoiceFooter = SettingsDefaults.InvoiceFooter,
                requiredBuyerFields = ContactOnly,
            });

            restore.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Staff_Cannot_Reach_The_Settings_Surface()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateStaffToken());

        using var response = await client.GetAsync($"{Base}/seller");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "these values decide what appears on every invoice");
    }
}
