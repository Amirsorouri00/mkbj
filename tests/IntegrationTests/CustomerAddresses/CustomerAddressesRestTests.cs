using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Location.Application.Domain.Enums;
using EwaysVNext.Modules.Location.Application.Domain.Locations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;

namespace EwaysVNext.IntegrationTests.CustomerAddresses;

/// <summary>
/// The customer address book over real HTTP + SQL Server LocalDB: ownership/IDOR, primary
/// (= default) address rules, validation, soft delete and IClock-driven timestamps.
///
/// <para>Auth: the shared fixture's JWT override targets the "Bearer" scheme, which the host's
/// multi-realm Keycloak router never uses, so it cannot authenticate anyone. This class swaps
/// in a header-driven test scheme that yields the same "identity_id" claim the real token
/// carries, which is all <c>IUserProfileService</c> reads.</para>
/// </summary>
[Collection(LocalDbSuite.Name)]
public sealed class CustomerAddressesRestTests : IDisposable
{
    private const string Base = "/api/v1/customers/me/addresses";

    // Location reference data, seeded once per database with ids far from anything real.
    private const int ProvinceId = 990001;
    private const int CityA = 990001;
    private const int DistrictOfA = 990001;
    private const int CityB = 990002;
    private const int DistrictOfB = 990002;

    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly MutableClock _clock = new(T0);

    public CustomerAddressesRestTests(LocalDbFixture fixture)
    {
        _factory = fixture.CreateFactory().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestIdentityHandler>(TestIdentityHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestIdentityHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestIdentityHandler.SchemeName;
                o.DefaultChallengeScheme = TestIdentityHandler.SchemeName;
            });
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(_clock);
        }));
        SeedLocationsAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _factory.Dispose();

    // ---------------------------------------------------------------- list / get

    [Fact]
    public async Task List_Returns_The_Primary_Address_And_Never_Another_Customers_Addresses()
    {
        var alice = await SeedCustomerAsync();
        var bob = await SeedCustomerAsync();
        var bobsExtra = await CreateAsync(bob, Body());

        var list = await ListAsync(alice);

        list.Should().ContainSingle();
        list[0].Id.Should().Be(alice.PrimaryAddressId);
        list[0].IsPrimary.Should().BeTrue();
        list.Select(a => a.Id).Should().NotContain([bob.PrimaryAddressId, bobsExtra.Id]);
    }

    [Fact]
    public async Task Get_Returns_Own_Address()
    {
        var alice = await SeedCustomerAsync();

        using var response = await Client(alice).GetAsync($"{Base}/{alice.PrimaryAddressId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync<AddressJson>(response)).IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task Unauthenticated_Caller_Is_Rejected()
    {
        using var client = _factory.CreateClient();
        (await client.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task Create_Persists_A_Non_Primary_Address_Owned_By_The_Caller()
    {
        var alice = await SeedCustomerAsync();

        var created = await CreateAsync(alice, Body(recipientName: "Ali Rezaei", recipientMobile: "09351112233"));

        created.IsPrimary.Should().BeFalse();
        created.Id.Should().BePositive();
        created.CityId.Should().Be(CityA);
        created.DistrictId.Should().Be(DistrictOfA);
        created.PostalCode.Should().Be("1234567890");
        created.RecipientName.Should().Be("Ali Rezaei");
        created.RecipientMobile.Should().Be("+989351112233");
        created.StatusId.Should().Be((int)AddressStatusType.Active);
        created.CreatedAt.Should().Be(T0.UtcDateTime);

        (await ListAsync(alice)).Select(a => a.Id).Should().Contain(created.Id);
        await WithDbAsync(async db =>
            (await db.Set<CustomerAddress>().SingleAsync(l => l.AddressId == created.Id)).CustomerId.Should().Be(alice.CustomerId));
    }

    [Fact]
    public async Task Create_Ignores_Client_Supplied_Id_Primary_And_Timestamps()
    {
        var alice = await SeedCustomerAsync();

        using var response = await Client(alice).PostAsJsonAsync(Base, new
        {
            id = 42, isPrimary = true, createdAt = "2000-01-01T00:00:00Z", customerId = 1,
            cityId = CityA, postalCode = "1234567890", isSelfRecipient = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await DataAsync<AddressJson>(response);
        created.Id.Should().NotBe(42);
        created.IsPrimary.Should().BeFalse();
        created.CreatedAt.Should().Be(T0.UtcDateTime);
        (await ListAsync(alice)).Count(a => a.IsPrimary).Should().Be(1);
    }

    [Fact]
    public async Task Self_Recipient_Takes_Name_And_Mobile_From_The_Profile_And_Leaves_The_Profile_Alone()
    {
        var alice = await SeedCustomerAsync();

        var created = await CreateAsync(alice, Body(isSelfRecipient: true, recipientName: "Someone Else", recipientMobile: "09390000000"));

        created.RecipientName.Should().Be("Sara Ahmadi");
        created.RecipientMobile.Should().Be(alice.Mobile);
        await WithDbAsync(async db =>
        {
            var c = await db.Customers.AsNoTracking().SingleAsync(x => x.Id == alice.CustomerId);
            c.FirstName.Should().Be("Sara");
            c.MobileNumber.Should().Be(alice.Mobile);
        });
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("12345-67890")]
    [InlineData("12345 67890")]
    [InlineData("abcdefghij")]
    [InlineData("")]
    public async Task Create_Rejects_An_Invalid_Postal_Code(string postalCode)
    {
        var alice = await SeedCustomerAsync();

        using var response = await Client(alice).PostAsJsonAsync(Base, Body(postalCode: postalCode));

        await ShouldFailAsync(response, HttpStatusCode.BadRequest, "INVALID_POSTAL_CODE");
        (await ListAsync(alice)).Should().ContainSingle("nothing may be created");
    }

    [Fact]
    public async Task Create_Rejects_An_Unknown_City_And_A_District_Of_Another_City()
    {
        var alice = await SeedCustomerAsync();

        using var badCity = await Client(alice).PostAsJsonAsync(Base, Body(cityId: 123_456_789, districtId: null));
        await ShouldFailAsync(badCity, HttpStatusCode.BadRequest, "INVALID_CITY");

        using var badDistrict = await Client(alice).PostAsJsonAsync(Base, Body(cityId: CityA, districtId: DistrictOfB));
        await ShouldFailAsync(badDistrict, HttpStatusCode.BadRequest, "INVALID_DISTRICT");
    }

    // ---------------------------------------------------------------- update

    [Fact]
    public async Task Update_Edits_A_Normal_Address_And_Stamps_UpdatedAt_From_IClock()
    {
        var alice = await SeedCustomerAsync();
        var created = await CreateAsync(alice, Body());
        _clock.UtcNow = T0.AddHours(3);

        using var response = await Client(alice).PutAsJsonAsync($"{Base}/{created.Id}",
            Body(cityId: CityB, districtId: DistrictOfB, postalCode: "0987654321", addressText: "New street"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await DataAsync<AddressJson>(response);
        updated.CityId.Should().Be(CityB);
        updated.DistrictId.Should().Be(DistrictOfB);
        updated.PostalCode.Should().Be("0987654321");
        updated.AddressText.Should().Be("New street");
        updated.CreatedAt.Should().Be(T0.UtcDateTime);
        updated.UpdatedAt.Should().Be(T0.AddHours(3).UtcDateTime);
    }

    [Fact]
    public async Task Update_Refuses_The_Primary_Address()
    {
        var alice = await SeedCustomerAsync();

        using var response = await Client(alice).PutAsJsonAsync($"{Base}/{alice.PrimaryAddressId}", Body(cityId: CityB, districtId: null));

        await ShouldFailAsync(response, HttpStatusCode.BadRequest, "PRIMARY_ADDRESS_CANNOT_BE_MODIFIED");
        await WithDbAsync(async db => (await db.Addresses.AsNoTracking().SingleAsync(a => a.Id == alice.PrimaryAddressId)).CityId.Should().Be(CityA));
    }

    // ---------------------------------------------------------------- delete

    [Fact]
    public async Task Delete_Soft_Deletes_A_Normal_Address()
    {
        var alice = await SeedCustomerAsync();
        var created = await CreateAsync(alice, Body());

        using var response = await Client(alice).DeleteAsync($"{Base}/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListAsync(alice)).Select(a => a.Id).Should().NotContain(created.Id);
        (await Client(alice).GetAsync($"{Base}/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await WithDbAsync(async db =>
            (await db.Addresses.AsNoTracking().SingleAsync(a => a.Id == created.Id)).StatusId.Should().Be((int)AddressStatusType.Deleted,
                "deletion follows the existing StatusId convention; the row is kept"));
    }

    [Fact]
    public async Task Delete_Refuses_The_Primary_Address()
    {
        var alice = await SeedCustomerAsync();

        using var response = await Client(alice).DeleteAsync($"{Base}/{alice.PrimaryAddressId}");

        await ShouldFailAsync(response, HttpStatusCode.BadRequest, "PRIMARY_ADDRESS_CANNOT_BE_MODIFIED");
        (await ListAsync(alice)).Should().ContainSingle(a => a.Id == alice.PrimaryAddressId && a.IsPrimary);
    }

    // ---------------------------------------------------------------- default (= primary)

    [Fact]
    public async Task Set_Default_Moves_The_Primary_Flag_And_Leaves_Exactly_One()
    {
        var alice = await SeedCustomerAsync();
        var created = await CreateAsync(alice, Body());

        using var response = await Client(alice).PutAsync($"{Base}/{created.Id}/default", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync<AddressJson>(response)).IsPrimary.Should().BeTrue();
        var list = await ListAsync(alice);
        list.Should().ContainSingle(a => a.IsPrimary).Which.Id.Should().Be(created.Id);
        list.Single(a => a.Id == alice.PrimaryAddressId).IsPrimary.Should().BeFalse("the previous default is unset");
    }

    [Fact]
    public async Task Set_Default_On_The_Current_Primary_Succeeds_And_Keeps_One_Primary()
    {
        var alice = await SeedCustomerAsync();
        await CreateAsync(alice, Body());

        using var response = await Client(alice).PutAsync($"{Base}/{alice.PrimaryAddressId}/default", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListAsync(alice)).Should().ContainSingle(a => a.IsPrimary).Which.Id.Should().Be(alice.PrimaryAddressId);
    }

    // ---------------------------------------------------------------- IDOR

    [Fact]
    public async Task Another_Customers_Address_Is_Not_Found_For_Get_Update_Delete_And_Default()
    {
        var alice = await SeedCustomerAsync();
        var bob = await SeedCustomerAsync();
        var bobs = await CreateAsync(bob, Body(addressText: "Bob's street"));
        var asAlice = Client(alice);

        await ShouldFailAsync(await asAlice.GetAsync($"{Base}/{bobs.Id}"), HttpStatusCode.NotFound, "ADDRESS_NOT_FOUND");
        await ShouldFailAsync(await asAlice.PutAsJsonAsync($"{Base}/{bobs.Id}", Body(addressText: "hijacked")), HttpStatusCode.NotFound, "ADDRESS_NOT_FOUND");
        await ShouldFailAsync(await asAlice.DeleteAsync($"{Base}/{bobs.Id}"), HttpStatusCode.NotFound, "ADDRESS_NOT_FOUND");
        await ShouldFailAsync(await asAlice.PutAsync($"{Base}/{bobs.Id}/default", null), HttpStatusCode.NotFound, "ADDRESS_NOT_FOUND");
        await ShouldFailAsync(await asAlice.PutAsync($"{Base}/{bob.PrimaryAddressId}/default", null), HttpStatusCode.NotFound, "ADDRESS_NOT_FOUND");

        var bobsView = await ListAsync(bob);
        var untouched = bobsView.Single(a => a.Id == bobs.Id);
        untouched.AddressText.Should().Be("Bob's street");
        untouched.StatusId.Should().Be((int)AddressStatusType.Active);
        untouched.IsPrimary.Should().BeFalse();
        bobsView.Single(a => a.Id == bob.PrimaryAddressId).IsPrimary.Should().BeTrue();
        (await ListAsync(alice)).Should().ContainSingle(a => a.IsPrimary && a.Id == alice.PrimaryAddressId);
    }

    // ================================================================ helpers

    private static object Body(
        int cityId = CityA, int? districtId = DistrictOfA, string? addressText = "Valiasr St, No. 12, Unit 3",
        string? postalCode = "1234567890", bool isSelfRecipient = false,
        string? recipientName = "Ali Rezaei", string? recipientMobile = "09351112233") =>
        new { cityId, districtId, addressText, postalCode, isSelfRecipient, recipientName, recipientMobile };

    private HttpClient Client(SeededCustomer who)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestIdentityHandler.Header, who.IdentityUserId.ToString());
        return client;
    }

    private async Task<AddressJson> CreateAsync(SeededCustomer who, object body)
    {
        using var response = await Client(who).PostAsJsonAsync(Base, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await DataAsync<AddressJson>(response);
    }

    private async Task<List<AddressJson>> ListAsync(SeededCustomer who)
    {
        using var response = await Client(who).GetAsync(Base);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await DataAsync<List<AddressJson>>(response);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<T> DataAsync<T>(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Deserialize<T>(Json)!;
    }

    private static async Task ShouldFailAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetString().Should().Be(code);
        response.Dispose();
    }

    private async Task WithDbAsync(Func<EwaysVNextDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>());
    }

    private async Task<SeededCustomer> SeedCustomerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();

        var mobile = MobileNumber.Create($"0912{Random.Shared.Next(0, 10_000_000):D7}");
        var customer = CustomerAggregate.Register(ids.Generate(), Guid.NewGuid(), mobile, T0);
        customer.RegisterCustomerInfo(NationalCode.Create("0012345679"), "Sara", "Ahmadi", Gender.Female, new DateOnly(1990, 1, 1), T0);

        // The registration address, exactly as CustomerService.InitializeAddress creates it.
        var primary = Address.Create(ids.Generate(), CityA, T0, DistrictOfA, "Registration address", isPrimary: true);
        db.Addresses.Add(primary);
        customer.AddAddress(CustomerAddress.Create(ids.Generate(), customer.Id, primary.Id));
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return new SeededCustomer(customer.Id, customer.IdentityUserId, mobile.Value, primary.Id);
    }

    private static readonly SemaphoreSlim SeedGate = new(1, 1);

    private async Task SeedLocationsAsync()
    {
        await SeedGate.WaitAsync();
        try
        {
            await WithDbAsync(db => db.Database.ExecuteSqlRawAsync($"""
                IF NOT EXISTS (SELECT 1 FROM [location].[Province] WHERE Id = {ProvinceId})
                BEGIN
                    SET IDENTITY_INSERT [location].[Province] ON;
                    INSERT INTO [location].[Province] (Id, Name, Code, IsActive) VALUES ({ProvinceId}, N'IT-Province-{ProvinceId}', NULL, 1);
                    SET IDENTITY_INSERT [location].[Province] OFF;
                    SET IDENTITY_INSERT [location].[City] ON;
                    INSERT INTO [location].[City] (Id, ProvinceId, Name, Code, IsActive) VALUES
                        ({CityA}, {ProvinceId}, N'IT-City-A', NULL, 1),
                        ({CityB}, {ProvinceId}, N'IT-City-B', NULL, 1);
                    SET IDENTITY_INSERT [location].[City] OFF;
                    SET IDENTITY_INSERT [location].[District] ON;
                    INSERT INTO [location].[District] (Id, CityId, Name, Code, IsActive) VALUES
                        ({DistrictOfA}, {CityA}, N'IT-District-A', NULL, 1),
                        ({DistrictOfB}, {CityB}, N'IT-District-B', NULL, 1);
                    SET IDENTITY_INSERT [location].[District] OFF;
                END
                """));
        }
        finally
        {
            SeedGate.Release();
        }
    }

    private sealed record SeededCustomer(long CustomerId, Guid IdentityUserId, string Mobile, long PrimaryAddressId);

    private sealed record AddressJson(
        long Id, int CityId, int? DistrictId, string? AddressText, string? PostalCode,
        string? RecipientName, string? RecipientMobile, bool IsPrimary, int StatusId,
        DateTime CreatedAt, DateTime? UpdatedAt);

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    /// <summary>Authenticates a request carrying <see cref="Header"/> as that identity user.</summary>
    private sealed class TestIdentityHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "CustomerAddressTests";
        public const string Header = "X-Test-Identity-Id";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Header, out var identityId))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim("identity_id", identityId.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
