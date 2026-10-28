using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using System.Text.RegularExpressions;

namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

public sealed class LegalInformation
{
    public long Id { get; private set; }
    public long CustomerId { get; private set; }
    public Customer? Customer { get; private set; }

    public string CompanyName { get; private set; } = string.Empty;
    public string NationalId { get; private set; } = string.Empty;
    public DateOnly RegistrationDate { get; private set; }
    public string RegistrationNumber { get; private set; } = string.Empty;
    public string CeoName { get; private set; } = string.Empty;
    public string CeoMobileNumber { get; private set; } = string.Empty;
    public string LandlineNumber { get; private set; } = string.Empty;
    public string? Fax { get; private set; }

    public int StatusId { get; private set; }
    public string? RejectionReason { get; private set; }
    
    public int? EditableFieldsMask { get; private set; }
    public long? ReviewerId { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyCollection<LegalInformationDocument> Documents => _documents.AsReadOnly();
    private readonly List<LegalInformationDocument> _documents = new();

    public LegalInformationStatusType Status => (LegalInformationStatusType)StatusId;
    public LegalInformationEditableField EditableFields => (LegalInformationEditableField)(EditableFieldsMask ?? 0);
    public bool CanSubmit => Status is LegalInformationStatusType.Draft or LegalInformationStatusType.Rejected;

    private LegalInformation() { }

    public static LegalInformation CreateDraft(long id, long customerId, DateTimeOffset now)
    {
        if (customerId <= 0)
            throw new DomainException("LEGALINFO_CUSTOMER_REQUIRED", "Customer ID is required.");

        var createdAt = now.UtcDateTime;
        return new LegalInformation
        {
            Id = id,
            CustomerId = customerId,
            StatusId = (int)LegalInformationStatusType.Draft,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    public bool IsFieldEditable(LegalInformationEditableField field) => Status switch
    {
        LegalInformationStatusType.Draft => true,
        LegalInformationStatusType.Rejected => EditableFields.HasFlag(field),
        _ => false
    };

    public void UpdateDetails(
        string? companyName,
        string? nationalId,
        DateOnly registrationDate,
        string? registrationNumber,
        string? ceoName,
        string? ceoMobileNumber,
        int provinceId,
        int cityId,
        int? districtId,
        string? address,
        string? postalCode,
        string? landlineNumber,
        string? fax,
        int currentCityId,
        int? currentDistrictId,
        string? currentAddress,
        string? currentPostalCode,
        DateTimeOffset now)
    {
        if (Status is LegalInformationStatusType.AwaitingReview or LegalInformationStatusType.Approved)
            throw new DomainException("LEGALINFO_LOCKED", "Legal information cannot be modified while awaiting review or after approval.");

        companyName ??= string.Empty;
        nationalId ??= string.Empty;
        registrationNumber ??= string.Empty;
        ceoName ??= string.Empty;
        address ??= string.Empty;
        postalCode ??= string.Empty;
        landlineNumber ??= string.Empty;

        ValidateCompanyName(companyName);
        ValidateNationalId(nationalId);
        ValidateRegistrationDate(registrationDate, now);
        ValidateRegistrationNumber(registrationNumber);
        var normalizedCeoMobile = ValidateCeoMobile(ceoMobileNumber);
        ValidatePostalCode(postalCode);
        ValidateLandline(landlineNumber);
        ValidateFax(fax);
        ValidateAddress(address);

        var rejected = Status == LegalInformationStatusType.Rejected;

        var currentAddressText = currentAddress ?? string.Empty;
        var currentPostal = currentPostalCode ?? string.Empty;

        GuardChange(rejected, LegalInformationEditableField.CompanyName, CompanyName, companyName);
        GuardChange(rejected, LegalInformationEditableField.NationalId, NationalId, nationalId);
        GuardChange(
            rejected,
            LegalInformationEditableField.RegistrationDate,
            RegistrationDate.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            registrationDate.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        GuardChange(rejected, LegalInformationEditableField.RegistrationNumber, RegistrationNumber, registrationNumber);
        GuardChange(rejected, LegalInformationEditableField.CeoName, CeoName, ceoName ?? string.Empty);
        GuardChange(rejected, LegalInformationEditableField.CeoMobileNumber, CeoMobileNumber, normalizedCeoMobile);
        GuardLocationChange(rejected, provinceId, cityId, districtId, currentCityId, currentDistrictId);
        GuardChange(rejected, LegalInformationEditableField.Address, currentAddressText, address);
        GuardChange(rejected, LegalInformationEditableField.PostalCode, currentPostal, postalCode);
        GuardChange(rejected, LegalInformationEditableField.LandlineNumber, LandlineNumber, landlineNumber);
        GuardChange(rejected, LegalInformationEditableField.Fax, Fax ?? string.Empty, fax ?? string.Empty);

        CompanyName = companyName;
        NationalId = nationalId;
        RegistrationDate = registrationDate;
        RegistrationNumber = registrationNumber;
        CeoName = ceoName ?? string.Empty;
        CeoMobileNumber = normalizedCeoMobile;
        LandlineNumber = landlineNumber;
        Fax = string.IsNullOrWhiteSpace(fax) ? null : fax;

        Touch(now);
    }

    public void AttachDocument(
        long id,
        LegalInformationDocumentType type,
        string storageKey,
        string fileName,
        string contentType,
        long sizeBytes,
        DateTimeOffset now)
    {
        if (Status is LegalInformationStatusType.AwaitingReview or LegalInformationStatusType.Approved)
            throw new DomainException("LEGALINFO_LOCKED", "Legal information cannot be modified while awaiting review or after approval.");

        var field = type == LegalInformationDocumentType.IntroductionLetter
            ? LegalInformationEditableField.IntroductionLetter
            : LegalInformationEditableField.OfficialGazette;

        if (Status == LegalInformationStatusType.Rejected && !EditableFields.HasFlag(field))
            throw new DomainException(
                "LEGALINFO_FIELD_NOT_EDITABLE",
                $"The '{type}' document is not editable until an expert flags it for correction.");

        var existing = _documents.FirstOrDefault(d => d.Type == (int)type);
        if (existing is not null)
            _documents.Remove(existing);

        _documents.Add(LegalInformationDocument.Create(id, Id, type, storageKey, fileName, contentType, sizeBytes, now));
        Touch(now);
    }

    public void EnsureReadyToSubmit()
    {
        if (Status is LegalInformationStatusType.AwaitingReview or LegalInformationStatusType.Approved)
            throw new DomainException(
                "LEGALINFO_ALREADY_SUBMITTED",
                "This legal information is already awaiting review or has already been approved.");

        EnsureComplete();
    }

    public void Submit(DateTimeOffset now)
    {
        EnsureReadyToSubmit();

        StatusId = (int)LegalInformationStatusType.AwaitingReview;
        RejectionReason = null;
        EditableFieldsMask = null;
        SubmittedAt = now.UtcDateTime;
        Touch(now);
    }

    public void Approve(long reviewerId, DateTimeOffset now)
    {
        if (Status != LegalInformationStatusType.AwaitingReview)
            throw new DomainException("LEGALINFO_NOT_AWAITING_REVIEW", "Only legal information awaiting review can be approved.");
        if (reviewerId <= 0)
            throw new DomainException("LEGALINFO_REVIEWER_REQUIRED", "A reviewing expert is required.");

        StatusId = (int)LegalInformationStatusType.Approved;
        ReviewerId = reviewerId;
        RejectionReason = null;
        EditableFieldsMask = null;
        DecidedAt = now.UtcDateTime;
        Touch(now);
    }

    public void Reject(long reviewerId, string reason, LegalInformationEditableField editableFields, DateTimeOffset now)
    {
        if (Status != LegalInformationStatusType.AwaitingReview)
            throw new DomainException("LEGALINFO_NOT_AWAITING_REVIEW", "Only legal information awaiting review can be rejected.");
        if (reviewerId <= 0)
            throw new DomainException("LEGALINFO_REVIEWER_REQUIRED", "A reviewing expert is required.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("LEGALINFO_REJECTION_REASON_REQUIRED", "A meaningful rejection reason is required.");
        if (editableFields == LegalInformationEditableField.None)
            throw new DomainException("LEGALINFO_REJECTION_FIELDS_REQUIRED", "At least one field must be flagged for correction.");

        StatusId = (int)LegalInformationStatusType.Rejected;
        ReviewerId = reviewerId;
        RejectionReason = reason;
        EditableFieldsMask = (int)editableFields;
        DecidedAt = now.UtcDateTime;
        Touch(now);
    }

    private void GuardChange(bool rejected, LegalInformationEditableField field, string current, string incoming)
    {
        if (!rejected || string.Equals(current, incoming, StringComparison.Ordinal))
            return;

        if (!EditableFields.HasFlag(field))
            throw new DomainException(
                "LEGALINFO_FIELD_NOT_EDITABLE",
                $"The '{field}' field is not editable until an expert flags it for correction.");
    }

    private void GuardLocationChange(bool rejected, int provinceId, int cityId, int? districtId, int currentCityId, int? currentDistrictId)
    {
        if (!rejected || (currentCityId == cityId && currentDistrictId == districtId))
            return;

        if (!EditableFields.HasFlag(LegalInformationEditableField.Location))
            throw new DomainException(
                "LEGALINFO_FIELD_NOT_EDITABLE",
                "The 'Location' field is not editable until an expert flags it for correction.");
    }

    private void EnsureComplete()
    {
        RequireNonEmpty(CompanyName, "CompanyName");
        RequireNonEmpty(NationalId, "NationalId");
        if (RegistrationDate == default)
            throw Required("RegistrationDate");
        RequireNonEmpty(RegistrationNumber, "RegistrationNumber");
        RequireNonEmpty(CeoName, "CeoName");
        RequireNonEmpty(CeoMobileNumber, "CeoMobileNumber");
        
        RequireNonEmpty(LandlineNumber, "LandlineNumber");

        if (!_documents.Any(d => d.Type == (int)LegalInformationDocumentType.IntroductionLetter))
            throw Required("IntroductionLetter");
        if (!_documents.Any(d => d.Type == (int)LegalInformationDocumentType.OfficialGazette))
            throw Required("OfficialGazette");
    }

    private static void RequireNonEmpty(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Required(field);
    }

    private static DomainException Required(string field) =>
        new($"LEGALINFO_{field.ToUpperInvariant()}_REQUIRED", $"{field} is required.");

    private static void ValidateCompanyName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        if (value.Length > 300)
            throw new DomainException("LEGALINFO_COMPANYNAME_TOO_LONG", "Company name cannot exceed 300 characters.");
    }

    private static void ValidateNationalId(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        CompanyNationalId.Create(value);
    }

    private static void ValidateRegistrationDate(DateOnly value, DateTimeOffset now)
    {
        if (value == default)
            return;
        if (value > DateOnly.FromDateTime(now.UtcDateTime))
            throw new DomainException("LEGALINFO_REGISTRATIONDATE_CANNOT_BE_FUTURE", "Registration date cannot be in the future.");
    }

    private static readonly Regex DigitsOnly = new(@"^[0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static void ValidateRegistrationNumber(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        if (!DigitsOnly.IsMatch(value))
            throw new DomainException("LEGALINFO_REGISTRATIONNUMBER_INVALID", "Numbers only");
    }

    private static string ValidateCeoMobile(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        try
        {
            return MobileNumber.Create(value).Value;
        }
        catch (DomainException)
        {
            throw new DomainException("LEGALINFO_CEOMOBILE_INVALID", "This isn't a valid mobile number");
        }
    }

    private static readonly Regex PostalCodePattern = new(@"^[0-9]{10}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static void ValidatePostalCode(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        if (!PostalCodePattern.IsMatch(value))
            throw new DomainException("LEGALINFO_POSTALCODE_INVALID", "Postal code must be exactly 10 digits, no dashes");
    }

    private static readonly Regex LandlinePattern = new(@"^0[0-9]{10}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static void ValidateLandline(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        if (!LandlinePattern.IsMatch(value))
            throw new DomainException("LEGALINFO_LANDLINE_INVALID", "That's not a valid landline number");
    }

    private static void ValidateFax(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        if (value.Length > 20)
            throw new DomainException("LEGALINFO_FAX_TOO_LONG", "Fax number cannot exceed 20 characters.");
    }

    private static void ValidateAddress(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        if (value.Length > 1000)
            throw new DomainException("LEGALINFO_ADDRESS_TOO_LONG", "Address cannot exceed 1000 characters.");
    }

    private void Touch(DateTimeOffset now) => UpdatedAt = now.UtcDateTime;
}
