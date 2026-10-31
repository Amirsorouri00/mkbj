using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Outbound;
using EwaysVNext.Modules.Customer.Application.Customers.Services;
using EwaysVNext.Persistence;
using NSubstitute;
using Xunit;

using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Customers.Domain.Customer;

namespace EwaysVNext.UnitTests.Customers;

public sealed class CustomerResolutionServiceTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;

    private readonly ICustomerRepository _repository = Substitute.For<ICustomerRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();

    private long _nextId = 100;

    public CustomerResolutionServiceTests()
    {
        _ids.Generate().Returns(_ => _nextId++);
        _uow.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(SaveOutcome.Saved);
    }

    private CustomerResolutionService Sut() =>
        new(_repository, _uow, _ids, new FixedClock(Now));

    private static CustomerAggregate Existing(long partnerId = 7, string external = "456") =>
        CustomerAggregate.CreatePartnerLinked(
            55, partnerId, external, Email.Create("a@x.com"), PhoneOf("+6591234567"), "Martin", Now);

    private static EwaysVNext.Modules.Customer.Application.Customers.Domain.Phone PhoneOf(string value)
    {
        EwaysVNext.Modules.Customer.Application.Customers.Domain.Phone.TryCreate(value, out var phone);
        return phone!;
    }

    [Fact]
    public async Task Create_Without_Any_Contact_Is_Customer_Contact_Required_And_Writes_Nothing()
    {
        var result = await Sut().CreatePartnerCustomerAsync(
            7, "456", new ContactInput(null, null, "Martin"), "corr-1");

        result.Error!.Code.Should().Be("CUSTOMER_CONTACT_REQUIRED");
        result.Error.HttpStatus.Should().Be(400);

        _repository.DidNotReceive().Add(Arg.Any<CustomerAggregate>());
        await _uow.DidNotReceive().TrySaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-an-email", null)]
    [InlineData(null, "91234567")]
    public async Task A_Malformed_Contact_Is_Refused_Rather_Than_Dropped(string? email, string? phone)
    {
        var result = await Sut().CreatePartnerCustomerAsync(
            7, "456", new ContactInput(email, phone, null), "corr-2");

        result.Error!.Code.Should().Be("VALIDATION_FAILED",
            "ignoring it would leave the partner believing they had supplied it");

        _repository.DidNotReceive().Add(Arg.Any<CustomerAggregate>());
    }

    [Fact]
    public async Task Sync_For_Another_Partners_Customer_Is_Not_Found()
    {
        _repository.GetAsync(55, Arg.Any<CancellationToken>()).Returns(Existing(partnerId: 7));

        var result = await Sut().SyncPartnerContactAsync(
            55, partnerId: 8, new ContactInput("b@x.com", null, null), "corr-3");

        result.Error!.Code.Should().Be("NOT_FOUND");
        result.Error.HttpStatus.Should().Be(404,
            "'forbidden' would confirm the id exists, which is enough to enumerate a "
            + "competitor's customers one id at a time");

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_After_Unique_Violation_Returns_The_Existing_Customer()
    {
        var existing = Existing();

        _uow.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(SaveOutcome.UniqueViolation);
        _repository.FindPartnerLinkedAsync(7, "456", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await Sut().CreatePartnerCustomerAsync(
            7, "456", new ContactInput("new@x.com", null, "Someone Else"), "corr-4");

        result.IsSuccess.Should().BeTrue();
        result.Value!.CustomerId.Should().Be(existing.Id);
        result.Value.Email.Should().Be("a@x.com",
            "the loser of the race reads the winner's row; create does not sync");
    }

    [Fact]
    public async Task An_Unchanged_Sync_Does_Not_Save()
    {
        _repository.GetAsync(55, Arg.Any<CancellationToken>()).Returns(Existing());

        var result = await Sut().SyncPartnerContactAsync(
            55, 7, new ContactInput("a@x.com", "+6591234567", "Martin"), "corr-5");

        result.IsSuccess.Should().BeTrue();

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Changed_Sync_Saves_Once()
    {
        _repository.GetAsync(55, Arg.Any<CancellationToken>()).Returns(Existing());

        await Sut().SyncPartnerContactAsync(55, 7, new ContactInput("b@x.com", null, null), "corr-6");

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sync_Accepts_A_Partial_Contact_Without_Requiring_Both_Channels()
    {
        _repository.GetAsync(55, Arg.Any<CancellationToken>()).Returns(Existing());

        var result = await Sut().SyncPartnerContactAsync(
            55, 7, new ContactInput(null, null, "Martin Tan"), "corr-7");

        result.IsSuccess.Should().BeTrue(
            "the contact requirement is about creating a customer, not about every later update");
    }

    [Fact]
    public async Task Invoice_Party_For_An_Unknown_Customer_Is_Not_Found()
    {
        _repository.GetAsync(999, Arg.Any<CancellationToken>()).Returns((CustomerAggregate?)null);

        (await Sut().GetInvoicePartyAsync(999)).Error!.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Find_Returns_Null_Rather_Than_An_Error_When_Absent()
    {
        _repository.FindPartnerLinkedAsync(7, "nope", Arg.Any<CancellationToken>())
            .Returns((CustomerAggregate?)null);

        (await Sut().FindPartnerCustomerAsync(7, "nope")).Should().BeNull(
            "not finding a customer is an answer, not a failure");
    }
}
