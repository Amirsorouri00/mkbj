using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;

namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;
public sealed class Customer : AggregateRoot<long>
{
    public Guid IdentityUserId { get; private set; }

    public string MobileNumber { get; private set; } = string.Empty;

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string NationalCode { get; private set; } = string.Empty;

    public Gender? Gender { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    public string? EconomicCode { get; private set; }

    public string? IntaCode { get; private set; }

    public string? RoleCode { get; private set; }

    public string? Email { get; private set; }

    public string? SecondaryPhoneNumber { get; private set; }

    public int? StatusId { get; private set; }

    public int? VerificationStatusId { get; private set; } 

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    public CustomerStatus CustomerStatusNavigation { get; private set; } = null!;
    public VerificationStatus VerificationStatusNavigation { get; private set; } = null!;
    public CustomerVerification Verification { get; private set; } = null!;

    public LegalInformation? LegalInformation { get; private set; }
    public IReadOnlyCollection<CustomerAddress> Addresses => _addresses.AsReadOnly();

    private readonly List<CustomerAddress> _addresses = new();

    public IReadOnlyCollection<CustomerJobType> JobTypes => _jobTypes.AsReadOnly();

    private readonly List<CustomerJobType> _jobTypes = new();
    public void AddAddress(CustomerAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        _addresses.Add(address);
    }
    public IList<CustomerAddress> GetAddresses()
       => _addresses.ToList();
    public CustomerAddress? GetAddress(int addressId)
       => _addresses.FirstOrDefault(c => c.AddressId == addressId);

    private Customer() { }
    public static Customer Register(
        long id,
        Guid IdentityUserId,
        MobileNumber mobileNumber,
        DateTimeOffset now)
    {
        if (IdentityUserId == Guid.Empty)
            throw new DomainException("KEYCLOAK_USERID_REQUIRED", CustomerMessages.KeycloakUserIdRequired);

        if (string.IsNullOrEmpty(mobileNumber.Value))
            throw new DomainException(" MOBILENUMBER_REQUIRED", CustomerMessages.MobileNumberRequired);

        var createdAt = now.UtcDateTime;

        return new Customer
        {
            Id = id,
            IdentityUserId = IdentityUserId,
            MobileNumber = mobileNumber.Value,
            VerificationStatusId = (int)VerificationStatusType.NotVerified,
            StatusId = (int)CustomerStatusType.PendingApproval,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }
    public void RegisterCustomerInfo(
        NationalCode nationalCode,
        string firstName,
        string lastName,
        Gender gender,
        DateOnly birthDate,
        DateTimeOffset now)
    {
        ValidatorProfiles(firstName, lastName, gender, birthDate, now);
        FirstName = firstName;
        LastName = lastName;
        Gender = gender;
        BirthDate = birthDate;
        NationalCode = nationalCode.Value;
        Touch(now);

    }
    public void Profile(string firstName,
        string lastName,
        Gender gender,
        DateOnly birthDate,
        DateTimeOffset now)
    {
        ValidatorProfiles(firstName,lastName,gender,birthDate, now);
        FirstName = firstName;
        LastName = lastName;
        Gender = gender;
        BirthDate = birthDate;
        Touch(now);
    }
    
    public void UpdateByAdmin(
        Gender gender,
        string? economicCode,
        string? intaCode,
        string? roleCode,
        Email? email,
        PhoneNumber? secondaryPhoneNumber,
        IReadOnlyCollection<(long Id, int JobTypeId)> newJobTypes,
        DateTimeOffset now)
    {
        if (!CanBeEditedByAdmin(StatusId))
            throw new DomainException("CUSTOMER_EDIT_NOT_ALLOWED_WHILE_AWAITING_EXPERT_CALL", CustomerMessages.EditNotAllowedWhileAwaitingExpertCall);

        if (!Enum.IsDefined(gender))
            throw new DomainException("INVALID_GENDER", CustomerMessages.InvalidGender);
        if (secondaryPhoneNumber is not null && secondaryPhoneNumber.Value == MobileNumber)
            throw new DomainException("SECONDARY_PHONENUMBER_SAME_AS_MOBILE", CustomerMessages.SecondaryPhoneNumberSameAsMobile);
        economicCode = NormalizeOptionalCode(economicCode, 10, "ECONOMICCODE_TOO_LONG", CustomerMessages.EconomicCodeTooLong);
        intaCode = NormalizeOptionalCode(intaCode, 20, "INTACODE_TOO_LONG", CustomerMessages.IntaCodeTooLong);
        roleCode = NormalizeOptionalCode(roleCode, 20, "ROLECODE_TOO_LONG", CustomerMessages.RoleCodeTooLong);

        Gender = gender;
        EconomicCode = economicCode;
        IntaCode = intaCode;
        RoleCode = roleCode;
        Email = email?.Value;
        SecondaryPhoneNumber = secondaryPhoneNumber?.Value;
        ReplaceJobTypes(newJobTypes);
        Touch(now);
    }

    public static bool CanBeEditedByAdmin(int? statusId) =>
        statusId != (int)CustomerStatusType.PendingApproval;

    private void ReplaceJobTypes(IReadOnlyCollection<(long Id, int JobTypeId)> jobTypes)
    {
        var requested = jobTypes.Select(j => j.JobTypeId).ToHashSet();
        _jobTypes.RemoveAll(j => !requested.Contains(j.JobTypeId));

        foreach (var (id, jobTypeId) in jobTypes.DistinctBy(j => j.JobTypeId))
        {
            if (_jobTypes.All(j => j.JobTypeId != jobTypeId))
                _jobTypes.Add(CustomerJobType.Create(id, Id, jobTypeId));
        }
    }

    private static string? NormalizeOptionalCode(string? value, int maxLength, string errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.Length > maxLength)
            throw new DomainException(errorCode, message);
        return value;
    }

    private static void ValidatorProfiles(string firstName,
        string lastName,
        Gender gender,
        DateOnly birthDate,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new DomainException("FIRSTNAME_REQUIRED", CustomerMessages.FirstNameRequired);
        else if (firstName.Length > 100)
            throw new DomainException("FIRSTNAME_TOO_LONG", CustomerMessages.FirstNameTooLong);
        if (string.IsNullOrWhiteSpace(lastName))
            throw new DomainException("LASTNAME_REQUIRED", CustomerMessages.LastNameRequired);
        else if (lastName.Length > 100)
            throw new DomainException("LASTNAME_TOO_LONG", CustomerMessages.LastNameTooLong);


        if (birthDate == DateOnly.MinValue)
            throw new DomainException("INVALID_BIRTH_DATE_FORMAT", CustomerMessages.InvalidBirthDateFormat);
        else if (birthDate >= DateOnly.FromDateTime(now.UtcDateTime))
            throw new DomainException("BIRTHDATE_CANNOT_THE_FUTURE", CustomerMessages.BirthDateCannotBeFuture);
    }

    public void UpdateVerificationStatus(VerificationStatusType status, DateTimeOffset now, int expertId=0)
    {
        if (StatusId == (int)CustomerStatusType.Approved && !IsVerifiedEnough(status))
            throw new DomainException(
                "CANNOT_UNVERIFY_AN_APPROVED_CUSTOMER",
                CustomerMessages.CannotUnverifyApprovedCustomer);

        if (VerificationStatusId == (int)VerificationStatusType.Pending && status == VerificationStatusType.Verified)
            Verification.MarkAsExpertVerified(expertId, now);
        VerificationStatusId = (int)status;
        Touch(now);
    }

    public void UpdateStatus(CustomerStatusType status, DateTimeOffset now)
    {
        if (status == CustomerStatusType.Approved && !IsVerifiedEnough((VerificationStatusType?)VerificationStatusId))
            throw new DomainException(
                "CUSTOMER_VERIFICATION_REQUIRED_FOR_APPROVAL",
                CustomerMessages.VerificationRequiredForApproval);

        StatusId = (int)status;
        Touch(now);
    }

    private static bool IsVerifiedEnough(VerificationStatusType? status) =>
        status is VerificationStatusType.Verified or VerificationStatusType.VerifiedByAgent;

    public void SetVerification(CustomerVerification verification, DateTimeOffset now)
    {
        Verification = verification;
        Touch(now);
    }

    /// <summary>
    /// Starts (or re-attaches) the customer's legal-information business profile.
    /// </summary>
    public void SetLegalInformation(LegalInformation legalInformation, DateTimeOffset now)
    {
        LegalInformation = legalInformation;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now.UtcDateTime;
    }
}
