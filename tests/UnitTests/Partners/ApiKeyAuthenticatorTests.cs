using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Partners.Application.Partners.Keys;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Outbound;
using EwaysVNext.Modules.Partners.Application.Partners.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Partners;

/// <summary>
/// Authentication refuses in exactly one way. Every failure returns null and says nothing about
/// which check failed: a caller must not be able to learn from the response whether a prefix
/// exists, whether a partner is suspended, or whether a key has merely expired, because each of
/// those is a probe someone can run at their leisure.
/// </summary>
public sealed class ApiKeyAuthenticatorTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;

    private readonly IApiKeyLookup _lookup = Substitute.For<IApiKeyLookup>();
    private readonly ApiKeyHasher _hasher = new(new ApiKeyPepper(System.Text.Encoding.UTF8.GetBytes(new string('p', 32))));
    private readonly GeneratedApiKey _key = ApiKeyFormat.Generate();

    private ApiKeyAuthenticator Sut() =>
        new(_lookup, _hasher, new FixedClock(Now), NullLogger<ApiKeyAuthenticator>.Instance);

    private ApiKeyCredential Credential(
        byte[]? hash = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? revokedAt = null,
        bool partnerActive = true) =>
        new(
            KeyId: 10,
            PartnerId: 1,
            SecretHash: hash ?? _hasher.Hash(_key.Secret),
            KeyScopes: [PartnerScopes.OrdersCreate],
            ExpiresAt: expiresAt,
            RevokedAt: revokedAt,
            PartnerActive: partnerActive,
            RateLimitPerMinute: 300,
            BulkMaxItems: 100);

    private void Returns(ApiKeyCredential? credential) =>
        _lookup.FindByPrefixAsync(_key.Prefix, Arg.Any<CancellationToken>()).Returns(credential);

    [Fact]
    public async Task Valid_Key_Returns_Partner_Scopes_And_Limits()
    {
        Returns(Credential());

        var result = await Sut().AuthenticateAsync(_key.Plaintext);

        result.Should().NotBeNull();
        result!.PartnerId.Should().Be(1);
        result.ApiKeyId.Should().Be(10);
        result.Scopes.Should().BeEquivalentTo([PartnerScopes.OrdersCreate]);
        result.RateLimitPerMinute.Should().Be(300);
        result.BulkMaxItems.Should().Be(100);
    }

    [Fact]
    public async Task Wrong_Secret_Returns_Null()
    {
        Returns(Credential(hash: _hasher.Hash("some-other-secret")));

        (await Sut().AuthenticateAsync(_key.Plaintext)).Should().BeNull();
    }

    [Fact]
    public async Task Malformed_Key_Returns_Null_Without_Lookup()
    {
        (await Sut().AuthenticateAsync("pk_bad")).Should().BeNull();

        await _lookup.DidNotReceiveWithAnyArgs().FindByPrefixAsync(default!, default);
    }

    [Fact]
    public async Task Unknown_Prefix_Returns_Null()
    {
        Returns(null);

        (await Sut().AuthenticateAsync(_key.Plaintext)).Should().BeNull();
    }

    [Fact]
    public async Task Revoked_Key_Returns_Null()
    {
        Returns(Credential(revokedAt: Now.AddMinutes(-1)));

        (await Sut().AuthenticateAsync(_key.Plaintext)).Should().BeNull();
    }

    [Fact]
    public async Task Key_At_Exact_Expiry_Returns_Null()
    {
        Returns(Credential(expiresAt: Now));

        (await Sut().AuthenticateAsync(_key.Plaintext)).Should().BeNull(
            "expiry is exclusive: 'expires at noon' means dead at noon");
    }

    [Fact]
    public async Task Key_One_Tick_Before_Expiry_Still_Works()
    {
        Returns(Credential(expiresAt: Now.AddTicks(1)));

        (await Sut().AuthenticateAsync(_key.Plaintext)).Should().NotBeNull();
    }

    [Fact]
    public async Task Suspended_Partner_Returns_Null()
    {
        Returns(Credential(partnerActive: false));

        (await Sut().AuthenticateAsync(_key.Plaintext)).Should().BeNull(
            "suspension must stop the keys without anyone having to revoke them one by one");
    }

    [Fact]
    public async Task A_Null_Or_Empty_Key_Returns_Null_Without_Lookup()
    {
        (await Sut().AuthenticateAsync(null!)).Should().BeNull();
        (await Sut().AuthenticateAsync(string.Empty)).Should().BeNull();

        await _lookup.DidNotReceiveWithAnyArgs().FindByPrefixAsync(default!, default);
    }
}
