using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;

namespace EwaysVNext.Modules.Customer.Application.Domain.TeamMembers;


public sealed class TeamMember : AggregateRoot<long>
{
    public Guid IdentityUserId { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public Gender Gender { get; private set; }

    public string NationalCode { get; private set; } = string.Empty;

    public DateOnly DateOfBirth { get; private set; }

    /// <summary>
    /// Also serves as the account's Keycloak username.
    /// </summary>
    public string MobileNumber { get; private set; } = string.Empty;

    public string PersonnelCode { get; private set; } = string.Empty;

    public string Department { get; private set; } = string.Empty;

    public string JobTitle { get; private set; } = string.Empty;

    public TeamMemberStatusType Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private TeamMember() { }

    public static TeamMember Create(
        long id,
        string firstName,
        string lastName,
        Gender gender,
        NationalCode nationalCode,
        DateOnly dateOfBirth,
        MobileNumber mobileNumber,
        string personnelCode,
        string department,
        string jobTitle,
        Guid identityUserId,
        DateTimeOffset now)
    {
        ValidateProfile(firstName, lastName, dateOfBirth, personnelCode, department, jobTitle, now);

        if (identityUserId == Guid.Empty)
            throw new DomainException("IDENTITYUSERID_REQUIRED", TeamMemberMessages.IdentityUserIdRequired);

        var createdAt = now.UtcDateTime;

        return new TeamMember
        {
            Id = id,
            IdentityUserId = identityUserId,
            FirstName = firstName,
            LastName = lastName,
            Gender = gender,
            NationalCode = nationalCode.Value,
            DateOfBirth = dateOfBirth,
            MobileNumber = mobileNumber.Value,
            PersonnelCode = personnelCode,
            Department = department,
            JobTitle = jobTitle,
            Status = TeamMemberStatusType.PendingAccess,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    public void UpdateProfile(
        string firstName,
        string lastName,
        Gender gender,
        NationalCode nationalCode,
        DateOnly dateOfBirth,
        MobileNumber mobileNumber,
        string personnelCode,
        string department,
        string jobTitle,
        DateTimeOffset now)
    {
        if (Status == TeamMemberStatusType.Deleted)
            throw new DomainException("TEAMMEMBER_DELETED_CANNOT_BE_EDITED", TeamMemberMessages.DeletedCannotBeEdited);

        ValidateProfile(firstName, lastName, dateOfBirth, personnelCode, department, jobTitle, now);

        FirstName = firstName;
        LastName = lastName;
        Gender = gender;
        NationalCode = nationalCode.Value;
        DateOfBirth = dateOfBirth;
        MobileNumber = mobileNumber.Value;
        PersonnelCode = personnelCode;
        Department = department;
        JobTitle = jobTitle;
        Touch(now);
    }

    public void Delete(DateTimeOffset now)
    {
        if (Status == TeamMemberStatusType.Deleted)
            throw new DomainException("TEAMMEMBER_ALREADY_DELETED", TeamMemberMessages.AlreadyDeleted);

        Status = TeamMemberStatusType.Deleted;
        Touch(now);
    }

    internal static void ValidateProfile(
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        string personnelCode,
        string department,
        string jobTitle,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new DomainException("FIRSTNAME_REQUIRED", TeamMemberMessages.FirstNameRequired);
        if (firstName.Length > 100)
            throw new DomainException("FIRSTNAME_TOO_LONG", TeamMemberMessages.FirstNameTooLong);

        if (string.IsNullOrWhiteSpace(lastName))
            throw new DomainException("LASTNAME_REQUIRED", TeamMemberMessages.LastNameRequired);
        if (lastName.Length > 100)
            throw new DomainException("LASTNAME_TOO_LONG", TeamMemberMessages.LastNameTooLong);

        if (dateOfBirth == DateOnly.MinValue)
            throw new DomainException("DATEOFBIRTH_REQUIRED", TeamMemberMessages.DateOfBirthRequired);
        if (dateOfBirth >= DateOnly.FromDateTime(now.UtcDateTime))
            throw new DomainException("DATEOFBIRTH_CANNOT_BE_FUTURE", TeamMemberMessages.DateOfBirthCannotBeFuture);

        if (string.IsNullOrWhiteSpace(personnelCode))
            throw new DomainException("PERSONNELCODE_REQUIRED", TeamMemberMessages.PersonnelCodeRequired);
        if (personnelCode.Length > 50)
            throw new DomainException("PERSONNELCODE_TOO_LONG", TeamMemberMessages.PersonnelCodeTooLong);

        if (string.IsNullOrWhiteSpace(department))
            throw new DomainException("DEPARTMENT_REQUIRED", TeamMemberMessages.DepartmentRequired);
        if (department.Length > 100)
            throw new DomainException("DEPARTMENT_TOO_LONG", TeamMemberMessages.DepartmentTooLong);

        if (string.IsNullOrWhiteSpace(jobTitle))
            throw new DomainException("JOBTITLE_REQUIRED", TeamMemberMessages.JobTitleRequired);
        if (jobTitle.Length > 100)
            throw new DomainException("JOBTITLE_TOO_LONG", TeamMemberMessages.JobTitleTooLong);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now.UtcDateTime;
    }
}
