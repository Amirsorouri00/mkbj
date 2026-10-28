using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using Xunit;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;

namespace EwaysVNext.UnitTests;

public class CustomerDomainTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();

    [Fact]
    public void Register_Creates_A_Customer_Pending_Verification_And_Pending_Registration_Approval()
    {
        var mobile = MobileNumber.Create("09123456789");
        var c = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(),mobile, TestTime.Now);

        c.MobileNumber.Should().Be(mobile.Value);
        c.VerificationStatusId.Should().Be((int)VerificationStatusType.NotVerified);
        c.StatusId.Should().Be((int)CustomerStatusType.PendingApproval);
    }

    [Fact]
    public void Register_Without_IdentityUser_Is_Rejected()
    {
        var mobile = MobileNumber.Create("09123456789");
        var act = () => CustomerAggregate.Register(IdGenerator.Generate(), Guid.Empty, mobile, TestTime.Now);
        act.Should().Throw<DomainException>().Where(e => e.Code == "KEYCLOAK_USERID_REQUIRED");
    }

    [Fact]
    public void RegisterCustomerInfo_Requires_A_FirstName()
    {
        var c = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(),MobileNumber.Create("09123456789"), TestTime.Now);
        var act = () => c.RegisterCustomerInfo(NationalCode.Create("0012345679"), "", "Doe", Gender.Male, new DateOnly(1990, 1, 1), TestTime.Now);
        act.Should().Throw<DomainException>().Where(e => e.Code == "FIRSTNAME_REQUIRED");
    }

    [Fact]
    public void RegisterCustomerInfo_Rejects_A_Future_BirthDate()
    {
        var c = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(),MobileNumber.Create("09123456789"), TestTime.Now);
        var act = () => c.RegisterCustomerInfo(NationalCode.Create("0012345679"), "John", "Doe", Gender.Male, DateOnly.FromDateTime(TestTime.PlusDays(1).UtcDateTime), TestTime.Now);
        act.Should().Throw<DomainException>().Where(e => e.Code == "BIRTHDATE_CANNOT_THE_FUTURE");
    }

    [Fact]
    public void RegisterCustomerInfo_Sets_The_Profile_Fields()
    {
        var c = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(),MobileNumber.Create("09123456789"), TestTime.Now);
        c.RegisterCustomerInfo(NationalCode.Create("0012345679"), "John", "Doe", Gender.Male, new DateOnly(1990, 1, 1), TestTime.Now);

        c.FirstName.Should().Be("John");
        c.LastName.Should().Be("Doe");
        c.NationalCode.Should().Be("0012345679");
    }

    [Theory]
    [InlineData("09123456789")]
    [InlineData("+989123456789")]
    [InlineData("00989123456789")]
    public void MobileNumber_Normalizes_Accepted_Formats_To_E164(string raw)
    {
        MobileNumber.Create(raw).Value.Should().Be("+989123456789");
    }

    [Fact]
    public void MobileNumber_Rejects_An_Invalid_Number()
    {
        var act = () => MobileNumber.Create("12345");
        act.Should().Throw<DomainException>().Where(e => e.Code == "MOBILENUMBER_CHECKSUM_IS_INVALID");
    }

    [Fact]
    public void NationalCode_Accepts_A_Valid_Checksum()
    {
        NationalCode.Create("0012345679").Value.Should().Be("0012345679");
    }

    [Fact]
    public void NationalCode_Rejects_An_Invalid_Checksum()
    {
        var act = () => NationalCode.Create("0012345678");
        act.Should().Throw<DomainException>().Where(e => e.Code == "NATIONALCODE_CHECKSUM_IS_INVALID");
    }

    [Fact]
    public void NationalCode_Rejects_The_Wrong_Length()
    {
        var act = () => NationalCode.Create("123");
        act.Should().Throw<DomainException>().Where(e => e.Code == "NATIONALCODE_MUST_BE_EXACTLY_TEN_DIGITS");
    }

    [Fact]
    public void UpdateStatus_Sets_The_StatusId_And_Leaves_Other_Fields_Untouched()
    {
        var c = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(),MobileNumber.Create("09123456789"), TestTime.Now);
        c.RegisterCustomerInfo(NationalCode.Create("0012345679"), "John", "Doe", Gender.Male, new DateOnly(1990, 1, 1), TestTime.Now);
        c.UpdateVerificationStatus(VerificationStatusType.Verified, TestTime.Now);

        c.UpdateStatus(CustomerStatusType.Approved, TestTime.Now);

        c.StatusId.Should().Be((int)CustomerStatusType.Approved);
        c.FirstName.Should().Be("John");
        c.LastName.Should().Be("Doe");
        c.VerificationStatusId.Should().Be((int)VerificationStatusType.Verified);
    }

    [Fact]
    public void UpdateStatus_Rejects_Approval_When_Identity_Is_Not_Verified()
    {
        var c = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(), MobileNumber.Create("09123456789"), TestTime.Now);

        var act = () => c.UpdateStatus(CustomerStatusType.Approved, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "CUSTOMER_VERIFICATION_REQUIRED_FOR_APPROVAL");
        c.StatusId.Should().Be((int)CustomerStatusType.PendingApproval, "a rejected approval must not change the registration status");
    }

    [Fact]
    public void UpdateStatus_Allows_Approval_When_Verified_By_A_Specialist()
    {
        var c = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(), MobileNumber.Create("09123456789"), TestTime.Now);
        c.UpdateVerificationStatus(VerificationStatusType.VerifiedByAgent, TestTime.Now, expertId: 1);

        c.UpdateStatus(CustomerStatusType.Approved, TestTime.Now);

        c.StatusId.Should().Be((int)CustomerStatusType.Approved);
    }

    [Fact]
    public void UpdateStatus_Does_Not_Require_Verification_To_Reject()
    {
        var c = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(), MobileNumber.Create("09123456789"), TestTime.Now);

        c.UpdateStatus(CustomerStatusType.Rejected, TestTime.Now);

        c.StatusId.Should().Be((int)CustomerStatusType.Rejected);
    }

    [Fact]
    public void UpdateVerificationStatus_Is_Independent_From_CustomerStatus()
    {
        var c = CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(),MobileNumber.Create("09123456789"), TestTime.Now);

        c.UpdateVerificationStatus(VerificationStatusType.Verified, TestTime.Now);

        c.VerificationStatusId.Should().Be((int)VerificationStatusType.Verified);
        c.StatusId.Should().Be((int)CustomerStatusType.PendingApproval);
    }
}
