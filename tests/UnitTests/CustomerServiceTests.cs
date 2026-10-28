using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using EwaysVNext.Modules.Customer.Application.Service;
using EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;
using EwaysVNext.Modules.Location.Contracts.Abstractions;
using EwaysVNext.Persistence;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;
using TeamMemberAggregate = EwaysVNext.Modules.Customer.Application.Domain.TeamMembers.TeamMember;

namespace EwaysVNext.UnitTests;

/// <summary>
/// Covers the admin-facing status/verification-status mutations (SHOP-315/SHOP-307 backend
/// support): CustomerService.UpdateStatusAsync / UpdateVerificationStatusAsync, including how a
/// rejection reason is now recorded directly on CustomerVerification (no separate history table).
/// </summary>
public class CustomerServiceTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();

    private readonly ICustomerRepository _repository = Substitute.For<ICustomerRepository>();
    private readonly ITeamMemberRepository _teamMembers = Substitute.For<ITeamMemberRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CustomerService _sut;

    public CustomerServiceTests()
    {
        _sut = new CustomerService(
            _repository,
            _teamMembers,
            _unitOfWork,
            Substitute.For<IShahkarService>(),
            Substitute.For<ILogger<CustomerService>>(),
            Substitute.For<ILocationService>(),
            new FixedClock(),
            IdGenerator);
    }

    private static CustomerAggregate ExistingCustomer()
    {
        var customer = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(), MobileNumber.Create("09123456789"), TestTime.Now);
        customer.RegisterCustomerInfo(NationalCode.Create("0012345679"), "John", "Doe", Gender.Male, new DateOnly(1990, 1, 1), TestTime.Now);
        customer.SetVerification(
            CustomerVerification.Create(IdGenerator.Generate(), customer.Id, "0012345679", new DateOnly(1990, 1, 1), TestTime.Now),
            TestTime.Now);
        return customer;
    }

    private static TeamMemberAggregate ExistingReviewer(Guid identityUserId) =>
        TeamMemberAggregate.Create(
            IdGenerator.Generate(), "Ali", "Ahmadi", Gender.Male,
            NationalCode.Create("0012345679"),
            new DateOnly(1990, 1, 1),
            MobileNumber.Create("09121110000"),
            "PC-100", "Support", "Agent",
            identityUserId, TestTime.Now);

    private void SetUpReviewer(TeamMemberAggregate reviewer) =>
        _teamMembers.GetByIdentityUserIdAsync(reviewer.IdentityUserId, Arg.Any<CancellationToken>()).Returns(reviewer);

    [Fact]
    public async Task UpdateStatusAsync_Existing_Customer_Succeeds_And_Persists_Only_The_Status()
    {
        var customer = ExistingCustomer();
        customer.UpdateVerificationStatus(VerificationStatusType.Verified, TestTime.Now);
        var reviewer = ExistingReviewer(Guid.NewGuid());
        SetUpReviewer(reviewer);
        _repository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _sut.UpdateStatusAsync(1, CustomerStatusType.Approved, reviewer.IdentityUserId);

        result.IsSuccess.Should().BeTrue();
        customer.StatusId.Should().Be((int)CustomerStatusType.Approved);
        customer.FirstName.Should().Be("John");
        customer.LastName.Should().Be("Doe");
        _repository.Received(1).Update(customer);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateStatusAsync_NonExisting_Customer_Returns_NotFound_And_Does_Not_Persist()
    {
        _repository.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((CustomerAggregate?)null);

        var result = await _sut.UpdateStatusAsync(99, CustomerStatusType.Approved, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("CUSTOMER_NOT_FOUND");
        _repository.DidNotReceive().Update(Arg.Any<CustomerAggregate>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateStatusAsync_Approval_Clears_Any_Stale_Rejection_Reason_On_The_Verification_Record()
    {
        var customer = ExistingCustomer();
        customer.UpdateVerificationStatus(VerificationStatusType.Verified, TestTime.Now);
        customer.Verification!.RecordRejection("An earlier, unrelated rejection.", TestTime.Now);
        var reviewer = ExistingReviewer(Guid.NewGuid());
        SetUpReviewer(reviewer);
        _repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _sut.UpdateStatusAsync(customer.Id, CustomerStatusType.Approved, reviewer.IdentityUserId);

        result.IsSuccess.Should().BeTrue();
        customer.StatusId.Should().Be((int)CustomerStatusType.Approved);
        customer.Verification!.Reason.Should().BeNull("an approval must not surface a stale rejection reason");
    }

    [Fact]
    public async Task UpdateStatusAsync_Rejection_Without_A_Reason_Fails_And_Does_Not_Persist()
    {
        var customer = ExistingCustomer();
        var reviewer = ExistingReviewer(Guid.NewGuid());
        SetUpReviewer(reviewer);
        _repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _sut.UpdateStatusAsync(customer.Id, CustomerStatusType.Rejected, reviewer.IdentityUserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("REJECTION_REASON_REQUIRED");
        customer.StatusId.Should().Be((int)CustomerStatusType.PendingApproval, "a failed rejection must not change the status");
        customer.Verification!.Reason.Should().BeNull("a failed rejection must not leave a stored reason");
        _repository.DidNotReceive().Update(Arg.Any<CustomerAggregate>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateStatusAsync_Rejection_With_A_Reason_Succeeds_And_Records_It_On_The_Verification_Record()
    {
        var customer = ExistingCustomer();
        var reviewer = ExistingReviewer(Guid.NewGuid());
        SetUpReviewer(reviewer);
        _repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _sut.UpdateStatusAsync(
            customer.Id, CustomerStatusType.Rejected, reviewer.IdentityUserId, reason: "National ID could not be verified.");

        result.IsSuccess.Should().BeTrue();
        customer.StatusId.Should().Be((int)CustomerStatusType.Rejected);
        customer.Verification!.Reason.Should().Be("National ID could not be verified.");
        _repository.Received(1).Update(customer);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact(Skip = "TODO(shop-321): the reviewer-existence check is commented out in CustomerService " +
        "(main commit 65231fb); restore this test once the check is restored.")]
    public async Task UpdateStatusAsync_Unresolvable_Reviewer_Fails_And_Does_Not_Persist()
    {
        var customer = ExistingCustomer();
        _repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        _teamMembers.GetByIdentityUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TeamMemberAggregate?)null);

        var result = await _sut.UpdateStatusAsync(customer.Id, CustomerStatusType.Approved, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("REVIEWING_TEAMMEMBER_NOT_FOUND");
        _repository.DidNotReceive().Update(Arg.Any<CustomerAggregate>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    #region AC-09: approval requires a successful identity verification

    [Fact]
    public async Task UpdateStatusAsync_Approve_Fails_When_NotVerified_And_Leaves_Everything_Unchanged()
    {
        var customer = ExistingCustomer(); // VerificationStatusId == NotVerified by default
        var registeredAt = customer.CreatedAt;
        var reviewer = ExistingReviewer(Guid.NewGuid());
        SetUpReviewer(reviewer);
        _repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _sut.UpdateStatusAsync(customer.Id, CustomerStatusType.Approved, reviewer.IdentityUserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CUSTOMER_VERIFICATION_REQUIRED_FOR_APPROVAL");
        customer.StatusId.Should().Be((int)CustomerStatusType.PendingApproval);
        customer.VerificationStatusId.Should().Be((int)VerificationStatusType.NotVerified);
        customer.CreatedAt.Should().Be(registeredAt, "a blocked approval must not touch the registration timestamp");
        customer.Verification!.Reason.Should().BeNull("a blocked approval must not leave a false rejection/approval record");
        _repository.DidNotReceive().Update(Arg.Any<CustomerAggregate>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateStatusAsync_Approve_Succeeds_When_VerifiedBySpecialist()
    {
        var customer = ExistingCustomer();
        customer.UpdateVerificationStatus(VerificationStatusType.VerifiedByAgent, TestTime.Now, expertId: 1);
        var reviewer = ExistingReviewer(Guid.NewGuid());
        SetUpReviewer(reviewer);
        _repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _sut.UpdateStatusAsync(customer.Id, CustomerStatusType.Approved, reviewer.IdentityUserId);

        result.IsSuccess.Should().BeTrue();
        customer.StatusId.Should().Be((int)CustomerStatusType.Approved);
    }

    [Fact]
    public async Task UpdateStatusAsync_Reject_Succeeds_While_NotVerified_Because_Rejection_Does_Not_Require_Verification()
    {
        var customer = ExistingCustomer(); // still NotVerified
        var reviewer = ExistingReviewer(Guid.NewGuid());
        SetUpReviewer(reviewer);
        _repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _sut.UpdateStatusAsync(
            customer.Id, CustomerStatusType.Rejected, reviewer.IdentityUserId, reason: "Did not pass review.");

        result.IsSuccess.Should().BeTrue();
        customer.StatusId.Should().Be((int)CustomerStatusType.Rejected);
        customer.Verification!.Reason.Should().Be("Did not pass review.");
    }

    [Theory]
    [InlineData(VerificationStatusType.NotVerified, false)]
    [InlineData(VerificationStatusType.Pending, false)]
    [InlineData(VerificationStatusType.Verified, true)]
    [InlineData(VerificationStatusType.VerifiedByAgent, true)]
    public async Task UpdateStatusAsync_Approval_Invariant_Only_Verified_Or_VerifiedByAgent_May_Approve(
        VerificationStatusType verificationStatus, bool approvalShouldSucceed)
    {
        var customer = ExistingCustomer();
        if (verificationStatus != VerificationStatusType.NotVerified)
            customer.UpdateVerificationStatus(verificationStatus, TestTime.Now, expertId: 1);
        var reviewer = ExistingReviewer(Guid.NewGuid());
        SetUpReviewer(reviewer);
        _repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _sut.UpdateStatusAsync(customer.Id, CustomerStatusType.Approved, reviewer.IdentityUserId);

        result.IsSuccess.Should().Be(approvalShouldSucceed);
        if (approvalShouldSucceed)
            customer.StatusId.Should().Be((int)CustomerStatusType.Approved);
        else
            customer.StatusId.Should().Be((int)CustomerStatusType.PendingApproval);
    }

    #endregion

    [Fact]
    public async Task UpdateVerificationStatusAsync_Existing_Customer_Succeeds_And_Persists_Only_The_VerificationStatus()
    {
        var customer = ExistingCustomer();
        var reviewer = ExistingReviewer(Guid.NewGuid());
        SetUpReviewer(reviewer);
        _repository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _sut.UpdateVerificationStatusAsync(1, 1, VerificationStatusType.VerifiedByAgent, reviewer.IdentityUserId);

        result.IsSuccess.Should().BeTrue();
        customer.VerificationStatusId.Should().Be((int)VerificationStatusType.VerifiedByAgent);
        customer.StatusId.Should().Be((int)CustomerStatusType.PendingApproval);
        _repository.Received(1).Update(customer);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateVerificationStatusAsync_NonExisting_Customer_Returns_NotFound_And_Does_Not_Persist()
    {
        _repository.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((CustomerAggregate?)null);

        var result = await _sut.UpdateVerificationStatusAsync(99, 1, VerificationStatusType.Verified, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("CUSTOMER_NOT_FOUND");
        _repository.DidNotReceive().Update(Arg.Any<CustomerAggregate>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact(Skip = "TODO(shop-321): the reviewer-existence check is commented out in CustomerService " +
        "(main commit 65231fb); restore this test once the check is restored.")]
    public async Task UpdateVerificationStatusAsync_Unresolvable_Reviewer_Fails_And_Does_Not_Persist()
    {
        var customer = ExistingCustomer();
        _repository.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        _teamMembers.GetByIdentityUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TeamMemberAggregate?)null);

        var result = await _sut.UpdateVerificationStatusAsync(customer.Id, 1, VerificationStatusType.Verified, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("REVIEWING_TEAMMEMBER_NOT_FOUND");
        _repository.DidNotReceive().Update(Arg.Any<CustomerAggregate>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
