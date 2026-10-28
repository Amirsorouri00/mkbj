using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using Xunit;
using TeamMemberAggregate = EwaysVNext.Modules.Customer.Application.Domain.TeamMembers.TeamMember;

namespace EwaysVNext.UnitTests;

public class TeamMemberDomainTests
{
    private static readonly Guid ValidIdentityUserId = Guid.NewGuid();
    private static readonly SnowflakeIdGenerator IdGenerator = new();

    private static TeamMemberAggregate CreateValid(Guid? identityUserId = null) =>
        TeamMemberAggregate.Create(
            IdGenerator.Generate(),
            "John",
            "Doe",
            Gender.Male,
            NationalCode.Create("0012345679"),
            new DateOnly(1990, 1, 1),
            MobileNumber.Create("09123456789"),
            "PC-001",
            "Engineering",
            "Support Specialist",
            identityUserId ?? ValidIdentityUserId,
            TestTime.Now);

    [Fact]
    public void Create_Sets_Initial_Status_To_PendingAccess()
    {
        var member = CreateValid();
        member.Status.Should().Be(TeamMemberStatusType.PendingAccess);
    }

    [Fact]
    public void Create_Sets_MobileNumber_As_The_Logical_Username()
    {
        var member = CreateValid();
        member.MobileNumber.Should().Be("+989123456789");
    }

    [Fact]
    public void Create_Stores_The_Real_Keycloak_IdentityUserId()
    {
        var identityUserId = Guid.NewGuid();
        var member = CreateValid(identityUserId);
        member.IdentityUserId.Should().Be(identityUserId);
    }

    [Fact]
    public void Create_Rejects_An_Empty_IdentityUserId()
    {
        var act = () => TeamMemberAggregate.Create(
            IdGenerator.Generate(), "John", "Doe", Gender.Male, NationalCode.Create("0012345679"),
            new DateOnly(1990, 1, 1), MobileNumber.Create("09123456789"),
            "PC-001", "Engineering", "Support Specialist", Guid.Empty, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "IDENTITYUSERID_REQUIRED");
    }

    [Fact]
    public void Create_Requires_A_FirstName()
    {
        var act = () => TeamMemberAggregate.Create(
            IdGenerator.Generate(), "", "Doe", Gender.Male, NationalCode.Create("0012345679"),
            new DateOnly(1990, 1, 1), MobileNumber.Create("09123456789"),
            "PC-001", "Engineering", "Support Specialist", ValidIdentityUserId, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "FIRSTNAME_REQUIRED");
    }

    [Fact]
    public void Create_Requires_A_LastName()
    {
        var act = () => TeamMemberAggregate.Create(
            IdGenerator.Generate(), "John", "", Gender.Male, NationalCode.Create("0012345679"),
            new DateOnly(1990, 1, 1), MobileNumber.Create("09123456789"),
            "PC-001", "Engineering", "Support Specialist", ValidIdentityUserId, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LASTNAME_REQUIRED");
    }

    [Fact]
    public void Create_Rejects_A_Future_DateOfBirth()
    {
        var act = () => TeamMemberAggregate.Create(
            IdGenerator.Generate(), "John", "Doe", Gender.Male, NationalCode.Create("0012345679"),
            DateOnly.FromDateTime(TestTime.PlusDays(1).UtcDateTime), MobileNumber.Create("09123456789"),
            "PC-001", "Engineering", "Support Specialist", ValidIdentityUserId, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "DATEOFBIRTH_CANNOT_BE_FUTURE");
    }

    [Fact]
    public void Create_Requires_A_PersonnelCode()
    {
        var act = () => TeamMemberAggregate.Create(
            IdGenerator.Generate(), "John", "Doe", Gender.Male, NationalCode.Create("0012345679"),
            new DateOnly(1990, 1, 1), MobileNumber.Create("09123456789"),
            "", "Engineering", "Support Specialist", ValidIdentityUserId, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "PERSONNELCODE_REQUIRED");
    }

    [Fact]
    public void Create_Requires_A_Department()
    {
        var act = () => TeamMemberAggregate.Create(
            IdGenerator.Generate(), "John", "Doe", Gender.Male, NationalCode.Create("0012345679"),
            new DateOnly(1990, 1, 1), MobileNumber.Create("09123456789"),
            "PC-001", "", "Support Specialist", ValidIdentityUserId, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "DEPARTMENT_REQUIRED");
    }

    [Fact]
    public void Create_Requires_A_JobTitle()
    {
        var act = () => TeamMemberAggregate.Create(
            IdGenerator.Generate(), "John", "Doe", Gender.Male, NationalCode.Create("0012345679"),
            new DateOnly(1990, 1, 1), MobileNumber.Create("09123456789"),
            "PC-001", "Engineering", "", ValidIdentityUserId, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "JOBTITLE_REQUIRED");
    }

    [Fact]
    public void UpdateProfile_Updates_Fields_Without_Touching_Status_Or_IdentityUserId()
    {
        var member = CreateValid();

        member.UpdateProfile(
            "Jane", "Smith", Gender.Female,
            NationalCode.Create("1234567806"),
            new DateOnly(1992, 2, 2),
            MobileNumber.Create("09129999999"),
            "PC-002", "Sales", "Account Manager", TestTime.Now);

        member.FirstName.Should().Be("Jane");
        member.LastName.Should().Be("Smith");
        member.Gender.Should().Be(Gender.Female);
        member.MobileNumber.Should().Be("+989129999999");
        member.PersonnelCode.Should().Be("PC-002");
        member.Department.Should().Be("Sales");
        member.JobTitle.Should().Be("Account Manager");
        member.Status.Should().Be(TeamMemberStatusType.PendingAccess);
        member.IdentityUserId.Should().Be(ValidIdentityUserId);
    }

    [Fact]
    public void UpdateProfile_Throws_When_The_Member_Is_Already_Deleted()
    {
        var member = CreateValid();
        member.Delete(TestTime.Now);

        var act = () => member.UpdateProfile(
            "Jane", "Smith", Gender.Female,
            NationalCode.Create("1234567806"),
            new DateOnly(1992, 2, 2),
            MobileNumber.Create("09129999999"),
            "PC-002", "Sales", "Account Manager", TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "TEAMMEMBER_DELETED_CANNOT_BE_EDITED");
    }

    [Fact]
    public void Delete_Sets_The_Status_To_Deleted()
    {
        var member = CreateValid();
        member.Delete(TestTime.Now);
        member.Status.Should().Be(TeamMemberStatusType.Deleted);
    }

    [Fact]
    public void Delete_Throws_When_Already_Deleted()
    {
        var member = CreateValid();
        member.Delete(TestTime.Now);

        var act = () => member.Delete(TestTime.Now);
        act.Should().Throw<DomainException>().Where(e => e.Code == "TEAMMEMBER_ALREADY_DELETED");
    }
}
