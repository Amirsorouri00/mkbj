using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.IntegrationTests.Support;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EwaysVNext.IntegrationTests.Partners;

/// <summary>
/// The partner credential path end to end: a real key, over HTTP, against a real database.
/// Nothing here is substituted except the clock, because a rotation overlap is measured in hours.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class PartnerApiKeyAuthenticationTests(PostgresFixture fixture)
{
    private static readonly string OrdersCreate =
        $"/test/partner/{PartnerProbeEndpoints.Slug(PartnerScopes.OrdersCreate)}";

    private readonly MutableClock _clock = new();

    private WebApplicationFactory<Program> CreateFactory() =>
        fixture.CreateFactory(configureServices: services =>
        {
            services.AddScoped<IEndpoint, PartnerProbeEndpoints>();
            services.Replace(ServiceDescriptor.Singleton<IClock>(_clock));
        });

    private static async Task<HttpStatusCode> Probe(
        WebApplicationFactory<Program> factory, string key, string url)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetWithBearerAsync(url, key);

        return response.StatusCode;
    }

    [Fact]
    public async Task Valid_Key_With_Scope_Returns_200()
    {
        using var factory = CreateFactory();
        var (_, _, key) = await PartnerSeeder.CreateAsync(factory, [PartnerScopes.OrdersCreate]);

        (await Probe(factory, key, OrdersCreate)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Wrong_Scope_Returns_403_Insufficient_Scope()
    {
        using var factory = CreateFactory();
        var (_, _, key) = await PartnerSeeder.CreateAsync(factory, [PartnerScopes.OrdersRead]);

        using var client = factory.CreateClient();
        using var response = await client.GetWithBearerAsync(OrdersCreate, key);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("INSUFFICIENT_SCOPE",
            "a partner with a valid key needs to know the key is too narrow, not merely that "
            + "they were refused");
        body.GetProperty("retryable").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Suspended_Partner_Key_Returns_401()
    {
        using var factory = CreateFactory();
        var (partnerId, _, key) = await PartnerSeeder.CreateAsync(factory, [PartnerScopes.OrdersCreate]);

        (await Probe(factory, key, OrdersCreate)).Should().Be(HttpStatusCode.OK);

        await PartnerSeeder.SuspendAsync(factory, partnerId);

        (await Probe(factory, key, OrdersCreate)).Should().Be(HttpStatusCode.Unauthorized,
            "suspension stops every key at once, without revoking them one by one");
    }

    [Fact]
    public async Task Revoked_Key_Returns_401()
    {
        using var factory = CreateFactory();
        var (partnerId, keyId, key) = await PartnerSeeder.CreateAsync(factory, [PartnerScopes.OrdersCreate]);

        (await Probe(factory, key, OrdersCreate)).Should().Be(HttpStatusCode.OK);

        await PartnerSeeder.RevokeAsync(factory, partnerId, keyId);

        (await Probe(factory, key, OrdersCreate)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rotated_Old_Key_Works_Until_Overlap_Expiry()
    {
        using var factory = CreateFactory();
        var (partnerId, keyId, oldKey) = await PartnerSeeder.CreateAsync(factory, [PartnerScopes.OrdersCreate]);

        var newKey = await PartnerSeeder.RotateAsync(factory, partnerId, keyId, TimeSpan.FromHours(1));

        (await Probe(factory, oldKey, OrdersCreate)).Should().Be(HttpStatusCode.OK,
            "rotation without an overlap is an outage: the old key would stop before the partner "
            + "had deployed the new one");

        _clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromSeconds(1));
        (await Probe(factory, oldKey, OrdersCreate)).Should().Be(HttpStatusCode.OK);

        // This proves the overlap ends. It deliberately does NOT pin whether expiry is inclusive
        // or exclusive: Postgres stores timestamptz to the microsecond while DateTimeOffset keeps
        // 100ns ticks, so the ExpiresAt read back here is not bit-identical to the one the
        // rotation computed, and the clock cannot be landed exactly on it. Mutating the
        // authenticator's `>=` to `>` leaves this test green. ApiKeyAuthenticatorTests
        // .Key_At_Exact_Expiry_Returns_Null is what holds that boundary, with no database in the
        // way — and it does fail under that mutation.
        _clock.Advance(TimeSpan.FromSeconds(1));
        (await Probe(factory, oldKey, OrdersCreate)).Should().Be(HttpStatusCode.Unauthorized);

        (await Probe(factory, newKey, OrdersCreate)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Malformed_Partner_Key_Returns_401()
    {
        using var factory = CreateFactory();

        (await Probe(factory, "pk_garbage", OrdersCreate)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Backoffice_Jwt_On_Partner_Endpoint_Returns_401()
    {
        using var factory = CreateFactory();

        (await Probe(factory, AdminJwtFactory.CreateSuperAdminToken(), OrdersCreate))
            .Should().Be(HttpStatusCode.Unauthorized,
                "a partner endpoint answers 'for this partner', and a human token names none");
    }

    [Fact]
    public async Task Partner_Rate_Limit_Returns_429_Rate_Limited()
    {
        using var factory = CreateFactory();
        var (_, _, key) = await PartnerSeeder.CreateAsync(
            factory, [PartnerScopes.OrdersCreate], rateLimit: 2);

        using var client = factory.CreateClient();

        (await client.GetWithBearerAsync(OrdersCreate, key)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetWithBearerAsync(OrdersCreate, key)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var rejected = await client.GetWithBearerAsync(OrdersCreate, key);

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();

        var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("RATE_LIMITED");
        body.GetProperty("retryable").GetBoolean().Should().BeTrue(
            "a rate limit is the one refusal that retrying does fix");
    }
}
