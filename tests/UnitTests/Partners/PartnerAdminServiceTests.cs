using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Partners.Application.Partners.Domain;
using EwaysVNext.Modules.Partners.Application.Partners.Keys;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Outbound;
using EwaysVNext.Modules.Partners.Application.Partners.Services;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Partners;

/// <summary>
/// Managing partners is super-admin work: issuing a key hands out the ability to place orders in
/// the seller's name, and widening scopes does the same more quietly.
/// </summary>
public sealed class PartnerAdminServiceTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;
    private static readonly AdminActor Super = new(Guid.NewGuid(), IsSuperAdmin: true);
    private static readonly AdminActor Staff = new(Guid.NewGuid(), IsSuperAdmin: false);

    private readonly IPartnerRepository _repository = Substitute.For<IPartnerRepository>();
    private readonly IAuditTrail _audit = Substitute.For<IAuditTrail>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly ApiKeyHasher _hasher = new(new ApiKeyPepper(System.Text.Encoding.UTF8.GetBytes(new string('p', 32))));

    private long _nextId = 100;

    public PartnerAdminServiceTests() => _ids.Generate().Returns(_ => _nextId++);

    private PartnerAdminService Sut(ApiKeyOptions? options = null) =>
        new(_repository, _hasher, _audit, _uow, _ids, new FixedClock(Now),
            Options.Create(options ?? new ApiKeyOptions()));

    private Partner Existing(params string[] scopes)
    {
        var partner = Partner.Create(1, "Acme",
            scopes.Length == 0 ? [PartnerScopes.OrdersCreate] : scopes, 300, 100, Now);

        _repository.GetAsync(1, Arg.Any<CancellationToken>()).Returns(partner);
        return partner;
    }

    [Fact]
    public async Task Staff_Actor_Is_Forbidden_And_Nothing_Is_Touched()
    {
        var result = await Sut().CreateAsync(
            new CreatePartnerCommand("Acme", [PartnerScopes.OrdersCreate], null, null), Staff);

        result.Error!.HttpStatus.Should().Be(403);
        _repository.ReceivedCalls().Should().BeEmpty();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _audit.ReceivedCalls().Should().BeEmpty("a refused action is not an action");
    }

    [Theory]
    [InlineData("suspend")]
    [InlineData("issue")]
    [InlineData("revoke")]
    public async Task Every_Operation_Refuses_Staff(string operation)
    {
        Existing();
        var sut = Sut();

        Result result = operation switch
        {
            "suspend" => await sut.SuspendAsync(1, Staff),
            "issue" => await sut.IssueKeyAsync(1, null, null, Staff),
            _ => await sut.RevokeKeyAsync(1, 10, Staff),
        };

        result.Error!.HttpStatus.Should().Be(403);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Uses_Spec_Defaults()
    {
        Partner? added = null;
        _repository.When(r => r.Add(Arg.Any<Partner>())).Do(call => added = call.Arg<Partner>());

        var result = await Sut().CreateAsync(
            new CreatePartnerCommand("Acme", [PartnerScopes.OrdersCreate], null, null), Super);

        result.IsSuccess.Should().BeTrue();
        added!.RateLimitPerMinute.Should().Be(PartnerLimits.DefaultRateLimitPerMinute);
        added.BulkMaxItems.Should().Be(PartnerLimits.DefaultBulkMaxItems);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Rejects_An_Unknown_Scope_As_A_Result_Not_An_Exception()
    {
        var result = await Sut().CreateAsync(
            new CreatePartnerCommand("Acme", ["orders:delete"], null, null), Super);

        result.Error!.Code.Should().Be("VALIDATION_FAILED");
        result.Error.HttpStatus.Should().Be(400,
            "a bad request is not a server fault, and a 500 would pollute the outage signal");
    }

    [Fact]
    public async Task IssueKey_Returns_Plaintext_Once_And_Stores_Only_The_Hash()
    {
        var partner = Existing();

        var issued = (await Sut().IssueKeyAsync(1, null, null, Super)).Value!;

        ApiKeyFormat.TryParse(issued.PlaintextKey, out var prefix, out var secret).Should().BeTrue();

        var stored = partner.Keys.Single();
        stored.Prefix.Should().Be(prefix);
        stored.SecretHash.Should().Equal(_hasher.Hash(secret));

        _audit.ReceivedCalls().SelectMany(call => call.GetArguments())
            .Select(argument => JsonSerializer.Serialize(argument))
            .Should().NotContain(serialized =>
                serialized.Contains(secret, StringComparison.Ordinal)
                || serialized.Contains(Convert.ToBase64String(stored.SecretHash), StringComparison.Ordinal),
                "an audit log is read by more people than the key ever should be");
    }

    [Fact]
    public async Task Rotate_Uses_Configured_Overlap_When_None_Given()
    {
        var partner = Existing();
        partner.IssueKey(10, "OLDPREF1", [9], null, null, Now);

        await Sut(new ApiKeyOptions { RotationOverlap = TimeSpan.FromHours(24) })
            .RotateKeyAsync(1, 10, overlap: null, Super);

        partner.Keys.Single(key => key.Id == 10).ExpiresAt.Should().Be(Now.AddHours(24));
    }

    [Fact]
    public async Task Rotate_Rejects_Overlap_Above_Max()
    {
        var partner = Existing();
        partner.IssueKey(10, "OLDPREF1", [9], null, null, Now);

        var result = await Sut(new ApiKeyOptions { MaxRotationOverlap = TimeSpan.FromDays(30) })
            .RotateKeyAsync(1, 10, TimeSpan.FromDays(31), Super);

        result.Error!.Code.Should().Be("VALIDATION_FAILED",
            "an overlap is a second live credential for its duration; a year's grace is not a rotation");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rotate_Rejects_A_Negative_Overlap()
    {
        var partner = Existing();
        partner.IssueKey(10, "OLDPREF1", [9], null, null, Now);

        (await Sut().RotateKeyAsync(1, 10, TimeSpan.FromSeconds(-1), Super))
            .Error!.Code.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Unknown_Partner_Is_Not_Found()
    {
        _repository.GetAsync(99, Arg.Any<CancellationToken>()).Returns((Partner?)null);

        var result = await Sut().SuspendAsync(99, Super);

        result.Error!.Code.Should().Be("NOT_FOUND");
        result.Error.HttpStatus.Should().Be(404);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revoking_An_Unknown_Key_Is_Not_Found()
    {
        Existing();

        (await Sut().RevokeKeyAsync(1, 999, Super)).Error!.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Every_Successful_Change_Is_Audited_Against_The_Partner()
    {
        Existing();

        await Sut().SuspendAsync(1, Super);

        _audit.Received(1).Record(
            Arg.Is<AuditActor>(actor => actor.UserId == Super.UserId),
            "partner.suspended",
            "Partner",
            "1",
            Arg.Any<object?>(),
            Arg.Any<object?>());
    }

    [Fact]
    public async Task SetScopes_Narrowing_Is_Audited_With_Before_And_After()
    {
        Existing(PartnerScopes.OrdersCreate, PartnerScopes.OrdersRead);

        await Sut().SetScopesAsync(1, [PartnerScopes.OrdersRead], Super);

        _audit.Received(1).Record(
            Arg.Any<AuditActor>(), "partner.scopes_set", "Partner", "1",
            Arg.Is<object?>(before => Serialized(before).Contains(PartnerScopes.OrdersCreate, StringComparison.Ordinal)),
            Arg.Is<object?>(after => !Serialized(after).Contains(PartnerScopes.OrdersCreate, StringComparison.Ordinal)));
    }

    private static string Serialized(object? value) => JsonSerializer.Serialize(value);
}
