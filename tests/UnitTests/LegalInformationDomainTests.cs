using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using Xunit;

namespace EwaysVNext.UnitTests;

public class LegalInformationDomainTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();

    private const string ValidCompanyName = "Eways Trading Co.";
    private const string ValidNationalId = "12345678901";
    private const string ValidRegistrationNumber = "998877";
    private const string ValidCeoName = "Sara Ahmadi";
    private const string ValidCeoMobile = "09123456789";
    private const string ValidAddress = "No. 1, Valiasr St., Tehran";
    private const string ValidPostalCode = "1234567890";
    private const string ValidLandline = "02112345678";

    private static LegalInformation NewDraft() => LegalInformation.CreateDraft(IdGenerator.Generate(), IdGenerator.Generate(), TestTime.Now);

    private static void FillValid(
        LegalInformation info, DateTimeOffset? now = null, int provinceId = 1, int cityId = 10, int? districtId = null,
        int currentCityId = 10, int? currentDistrictId = null, string currentAddress = ValidAddress, string currentPostalCode = ValidPostalCode)
    {
        var effectiveNow = now ?? TestTime.Now;
        info.UpdateDetails(
            ValidCompanyName, ValidNationalId, new DateOnly(2015, 1, 1), ValidRegistrationNumber,
            ValidCeoName, ValidCeoMobile, provinceId, cityId, districtId,
            ValidAddress, ValidPostalCode, ValidLandline, fax: null,
            currentCityId, currentDistrictId, currentAddress, currentPostalCode, effectiveNow);
    }

    private static void AttachBothDocuments(LegalInformation info, DateTimeOffset? now = null)
    {
        info.AttachDocument(IdGenerator.Generate(), LegalInformationDocumentType.IntroductionLetter, "key-1", "letter.pdf", "application/pdf", 1024, now ?? TestTime.Now);
        info.AttachDocument(IdGenerator.Generate(), LegalInformationDocumentType.OfficialGazette, "key-2", "gazette.pdf", "application/pdf", 1024, now ?? TestTime.Now);
    }

    [Fact]
    public void CreateDraft_Starts_In_Draft_Status_With_No_Details()
    {
        var info = NewDraft();

        info.Status.Should().Be(LegalInformationStatusType.Draft);
        info.CanSubmit.Should().BeTrue();
        info.CompanyName.Should().BeEmpty();
    }

    [Fact]
    public void UpdateDetails_Sets_All_Fields_When_Valid()
    {
        var info = NewDraft();

        FillValid(info);

        info.CompanyName.Should().Be(ValidCompanyName);
        info.NationalId.Should().Be(ValidNationalId);
        info.CeoMobileNumber.Should().Be("+989123456789");
        info.LandlineNumber.Should().Be(ValidLandline);
    }

    [Theory]
    [InlineData("1234567890A")] // not numeric
    [InlineData("123456789")]   // 9 digits
    [InlineData("123456789012")] // 12 digits
    public void UpdateDetails_Rejects_An_Invalid_NationalId(string invalidNationalId)
    {
        var info = NewDraft();

        var act = () => info.UpdateDetails(
            ValidCompanyName, invalidNationalId, new DateOnly(2015, 1, 1), ValidRegistrationNumber,
            ValidCeoName, ValidCeoMobile, 1, 10, null, ValidAddress, ValidPostalCode, ValidLandline, null,
            10, null, ValidAddress, ValidPostalCode, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_NATIONALID_INVALID"
            && e.Message == "National ID must be exactly 11 digits, numbers only");
    }

    [Fact]
    public void UpdateDetails_Rejects_A_NonNumeric_RegistrationNumber()
    {
        var info = NewDraft();

        var act = () => info.UpdateDetails(
            ValidCompanyName, ValidNationalId, new DateOnly(2015, 1, 1), "98-77",
            ValidCeoName, ValidCeoMobile, 1, 10, null, ValidAddress, ValidPostalCode, ValidLandline, null,
            10, null, ValidAddress, ValidPostalCode, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_REGISTRATIONNUMBER_INVALID" && e.Message == "Numbers only");
    }

    [Fact]
    public void UpdateDetails_Rejects_A_Future_RegistrationDate()
    {
        var info = NewDraft();
        var future = DateOnly.FromDateTime(TestTime.PlusDays(1).UtcDateTime);

        var act = () => info.UpdateDetails(
            ValidCompanyName, ValidNationalId, future, ValidRegistrationNumber,
            ValidCeoName, ValidCeoMobile, 1, 10, null, ValidAddress, ValidPostalCode, ValidLandline, null,
            10, null, ValidAddress, ValidPostalCode, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_REGISTRATIONDATE_CANNOT_BE_FUTURE");
    }

    [Fact]
    public void UpdateDetails_Rejects_An_Invalid_Ceo_Mobile_Number()
    {
        var info = NewDraft();

        var act = () => info.UpdateDetails(
            ValidCompanyName, ValidNationalId, new DateOnly(2015, 1, 1), ValidRegistrationNumber,
            ValidCeoName, "12345", 1, 10, null, ValidAddress, ValidPostalCode, ValidLandline, null,
            10, null, ValidAddress, ValidPostalCode, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_CEOMOBILE_INVALID" && e.Message == "This isn't a valid mobile number");
    }

    [Theory]
    [InlineData("123456789")]     // 9 digits
    [InlineData("1234-56789")]    // contains a dash
    [InlineData("12345678901")]   // 11 digits
    public void UpdateDetails_Rejects_An_Invalid_PostalCode(string invalidPostalCode)
    {
        var info = NewDraft();

        var act = () => info.UpdateDetails(
            ValidCompanyName, ValidNationalId, new DateOnly(2015, 1, 1), ValidRegistrationNumber,
            ValidCeoName, ValidCeoMobile, 1, 10, null, ValidAddress, invalidPostalCode, ValidLandline, null,
            10, null, ValidAddress, ValidPostalCode, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_POSTALCODE_INVALID"
            && e.Message == "Postal code must be exactly 10 digits, no dashes");
    }

    [Theory]
    [InlineData("2112345678")]  // missing leading zero
    [InlineData("021123456")]  // too short
    public void UpdateDetails_Rejects_An_Invalid_Landline(string invalidLandline)
    {
        var info = NewDraft();

        var act = () => info.UpdateDetails(
            ValidCompanyName, ValidNationalId, new DateOnly(2015, 1, 1), ValidRegistrationNumber,
            ValidCeoName, ValidCeoMobile, 1, 10, null, ValidAddress, ValidPostalCode, invalidLandline, null,
            10, null, ValidAddress, ValidPostalCode, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_LANDLINE_INVALID" && e.Message == "That's not a valid landline number");
    }

    [Fact]
    public void UpdateDetails_Is_Locked_While_AwaitingReview()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);
        info.Submit(TestTime.Now);

        var act = () => FillValid(info);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_LOCKED");
    }

    [Fact]
    public void UpdateDetails_Is_Locked_After_Approval()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);
        info.Submit(TestTime.Now);
        info.Approve(reviewerId: 1, TestTime.Now);

        var act = () => FillValid(info);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_LOCKED");
    }

    [Fact]
    public void UpdateDetails_After_Rejection_Allows_Only_The_Flagged_Fields()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);
        info.Submit(TestTime.Now);
        info.Reject(reviewerId: 1, "The company name does not match the gazette.", LegalInformationEditableField.CompanyName, TestTime.Now);

        var act = () => info.UpdateDetails(
            "A Different Company", ValidNationalId, new DateOnly(2015, 1, 1), ValidRegistrationNumber,
            ValidCeoName, ValidCeoMobile, 1, 10, null, ValidAddress, ValidPostalCode, ValidLandline, null,
            10, null, ValidAddress, ValidPostalCode, TestTime.Now);

        act.Should().NotThrow();
        info.CompanyName.Should().Be("A Different Company");
    }

    [Fact]
    public void UpdateDetails_After_Rejection_Rejects_Changes_To_NonFlagged_Fields()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);
        info.Submit(TestTime.Now);
        info.Reject(reviewerId: 1, "The company name does not match the gazette.", LegalInformationEditableField.CompanyName, TestTime.Now);

        var act = () => info.UpdateDetails(
            ValidCompanyName, ValidNationalId, new DateOnly(2015, 1, 1), "999999",
            ValidCeoName, ValidCeoMobile, 1, 10, null, ValidAddress, ValidPostalCode, ValidLandline, null,
            10, null, ValidAddress, ValidPostalCode, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_FIELD_NOT_EDITABLE");
    }

    [Fact]
    public void UpdateDetails_After_Rejection_Allows_Resending_Unchanged_NonFlagged_Fields()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);
        info.Submit(TestTime.Now);
        info.Reject(reviewerId: 1, "reason", LegalInformationEditableField.CompanyName, TestTime.Now);

        var act = () => FillValid(info); // same values everywhere, including the non-flagged ones

        act.Should().NotThrow();
    }

    [Fact]
    public void AttachDocument_Is_Locked_While_AwaitingReview()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);
        info.Submit(TestTime.Now);

        var act = () => info.AttachDocument(IdGenerator.Generate(), LegalInformationDocumentType.IntroductionLetter, "k", "f.pdf", "application/pdf", 10, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_LOCKED");
    }

    [Fact]
    public void AttachDocument_After_Rejection_Requires_The_Document_To_Be_Flagged()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);
        info.Submit(TestTime.Now);
        info.Reject(reviewerId: 1, "reason", LegalInformationEditableField.CompanyName, TestTime.Now);

        var act = () => info.AttachDocument(IdGenerator.Generate(), LegalInformationDocumentType.IntroductionLetter, "k", "f.pdf", "application/pdf", 10, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_FIELD_NOT_EDITABLE");
    }

    [Fact]
    public void Submit_Fails_When_A_Required_Field_Is_Missing()
    {
        var info = NewDraft();
        AttachBothDocuments(info);

        var act = () => info.Submit(TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_COMPANYNAME_REQUIRED");
    }

    [Fact]
    public void Submit_Fails_When_A_Required_Document_Is_Missing()
    {
        var info = NewDraft();
        FillValid(info);
        info.AttachDocument(IdGenerator.Generate(), LegalInformationDocumentType.IntroductionLetter, "key-1", "letter.pdf", "application/pdf", 1024, TestTime.Now);

        var act = () => info.Submit(TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_OFFICIALGAZETTE_REQUIRED");
    }

    [Fact]
    public void Submit_Transitions_To_AwaitingReview_And_Clears_Any_Prior_Rejection_State()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);

        info.Submit(TestTime.Now);

        info.Status.Should().Be(LegalInformationStatusType.AwaitingReview);
        info.CanSubmit.Should().BeFalse();
        info.RejectionReason.Should().BeNull();
        info.SubmittedAt.Should().Be(TestTime.Now.UtcDateTime);
    }

    [Fact]
    public void Submit_Fails_When_Already_AwaitingReview()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);
        info.Submit(TestTime.Now);

        var act = () => info.Submit(TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_ALREADY_SUBMITTED");
    }

    [Fact]
    public void Approve_Fails_When_Not_AwaitingReview()
    {
        var info = NewDraft();

        var act = () => info.Approve(reviewerId: 1, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_NOT_AWAITING_REVIEW");
    }

    [Fact]
    public void Approve_Sets_Approved_Records_The_Reviewer_And_Decision_Time()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);
        info.Submit(TestTime.Now);

        info.Approve(reviewerId: 42, TestTime.PlusMinutes(5));

        info.Status.Should().Be(LegalInformationStatusType.Approved);
        info.ReviewerId.Should().Be(42);
        info.DecidedAt.Should().Be(TestTime.PlusMinutes(5).UtcDateTime);
        info.RejectionReason.Should().BeNull();
    }

    [Fact]
    public void Reject_Fails_When_Not_AwaitingReview()
    {
        var info = NewDraft();

        var act = () => info.Reject(1, "reason", LegalInformationEditableField.CompanyName, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_NOT_AWAITING_REVIEW");
    }

    [Fact]
    public void Reject_Requires_A_Meaningful_Reason()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);
        info.Submit(TestTime.Now);

        var act = () => info.Reject(1, "   ", LegalInformationEditableField.CompanyName, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_REJECTION_REASON_REQUIRED");
    }

    [Fact]
    public void Reject_Requires_At_Least_One_Flagged_Field()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);
        info.Submit(TestTime.Now);

        var act = () => info.Reject(1, "Not good enough.", LegalInformationEditableField.None, TestTime.Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_REJECTION_FIELDS_REQUIRED");
    }

    [Fact]
    public void Reject_Sets_Rejected_With_Reason_And_Editable_Fields()
    {
        var info = NewDraft();
        FillValid(info);
        AttachBothDocuments(info);
        info.Submit(TestTime.Now);

        info.Reject(7, "The registration number could not be verified.", LegalInformationEditableField.RegistrationNumber, TestTime.Now);

        info.Status.Should().Be(LegalInformationStatusType.Rejected);
        info.ReviewerId.Should().Be(7);
        info.RejectionReason.Should().Be("The registration number could not be verified.");
        info.IsFieldEditable(LegalInformationEditableField.RegistrationNumber).Should().BeTrue();
        info.IsFieldEditable(LegalInformationEditableField.CompanyName).Should().BeFalse();
        info.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void CompanyNationalId_Accepts_Exactly_Eleven_Digits()
    {
        CompanyNationalId.Create("12345678901").Value.Should().Be("12345678901");
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("1234567890A")]
    [InlineData("")]
    public void CompanyNationalId_Rejects_Anything_Else(string value)
    {
        var act = () => CompanyNationalId.Create(value);
        act.Should().Throw<DomainException>().Where(e => e.Code == "LEGALINFO_NATIONALID_INVALID");
    }
}
