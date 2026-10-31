using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Settings.Application.Settings.Domain;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Outbound;
using EwaysVNext.Modules.Settings.Application.Settings.Services;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Settings;

public sealed class SettingsAdminServiceTests
{
    private static readonly AdminActor Super = new(Guid.NewGuid(), IsSuperAdmin: true);
    private static readonly AdminActor Staff = new(Guid.NewGuid(), IsSuperAdmin: false);

    private readonly ISettingsRepository _repository = Substitute.For<ISettingsRepository>();
    private readonly IAuditTrail _audit = Substitute.For<IAuditTrail>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();

    public SettingsAdminServiceTests() => _ids.Generate().Returns(_ => 500L);

    private SettingsAdminService Sut() =>
        new(_repository, _audit, _uow, _ids, new FixedClock(TestTime.Now));

    private PaymentInstruction ExistingInstruction()
    {
        var instruction = PaymentInstruction.CreateDefault(TestTime.Now);
        _repository.GetPaymentInstructionAsync(true, Arg.Any<CancellationToken>()).Returns(instruction);
        return instruction;
    }

    private SellerProfile ExistingSeller()
    {
        var seller = SellerProfile.CreateDefault();
        _repository.GetSellerAsync(true, Arg.Any<CancellationToken>()).Returns(seller);
        return seller;
    }

    private static UpdateSellerCommand SellerCommand(bool gstRegistered = false) =>
        new("Acme Pte Ltd", "202012345A", "1 Raffles Place", "billing@acme.sg",
            gstRegistered, "Thank you", ["email|phone"]);

    [Fact]
    public async Task Staff_Actor_Is_Forbidden_On_Every_Method()
    {
        var sut = Sut();

        var results = new List<Result>
        {
            await sut.UpdateSellerAsync(SellerCommand(), Staff),
            await sut.PublishPaymentInstructionAsync("text", Staff),
            await sut.ActivatePaymentInstructionAsync(1, Staff),
            await sut.UpdateCommerceLimitsAsync(new UpdateCommerceLimitsCommand(1000, 2000, 10, 8000, 10), Staff),
        };

        results.Should().OnlyContain(result => result.Error!.HttpStatus == 403);

        _repository.ReceivedCalls().Should().BeEmpty("a refused caller must not even load the row");
        _audit.ReceivedCalls().Should().BeEmpty();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_Records_Audit_With_Version_And_Text()
    {
        ExistingInstruction();

        var result = await Sut().PublishPaymentInstructionAsync("Wire to DBS 123", Super);

        result.Value.Should().Be(2);

        _audit.Received(1).Record(
            Arg.Is<AuditActor>(actor => actor.UserId == Super.UserId),
            "settings.payment_instruction_published",
            "Settings",
            "1",
            // A matcher rather than a literal null: NSubstitute cannot mix the two across
            // arguments of the same type, and a publish has no "before" state to record.
            Arg.Is<object?>(before => before == null),
            Arg.Is<object?>(after =>
                JsonSerializer.Serialize(after).Contains("Wire to DBS 123", StringComparison.Ordinal)));

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gst_Registration_Is_A_Result_Not_An_Exception()
    {
        ExistingSeller();

        var result = await Sut().UpdateSellerAsync(SellerCommand(gstRegistered: true), Super);

        result.Error!.Code.Should().Be("VALIDATION_FAILED");
        result.Error.HttpStatus.Should().Be(400,
            "a refused setting is a bad request, not a server fault");

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Activating_An_Unknown_Version_Is_Not_Found()
    {
        ExistingInstruction();

        (await Sut().ActivatePaymentInstructionAsync(99, Super)).Error!.Code.Should().Be("NOT_FOUND");

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seller_Update_Is_Audited_With_Before_And_After()
    {
        ExistingSeller();

        await Sut().UpdateSellerAsync(SellerCommand(), Super);

        _audit.Received(1).Record(
            Arg.Any<AuditActor>(), "settings.seller_updated", "Settings", "1",
            Arg.Is<object?>(before =>
                JsonSerializer.Serialize(before).Contains(SettingsDefaults.LegalName, StringComparison.Ordinal)),
            Arg.Is<object?>(after =>
                JsonSerializer.Serialize(after).Contains("Acme Pte Ltd", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_Missing_Singleton_Is_Not_Found_Rather_Than_A_Crash()
    {
        _repository.GetSellerAsync(true, Arg.Any<CancellationToken>()).Returns((SellerProfile?)null);

        (await Sut().UpdateSellerAsync(SellerCommand(), Super)).Error!.Code.Should().Be("NOT_FOUND");
    }
}
