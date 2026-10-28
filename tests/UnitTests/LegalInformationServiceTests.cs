using AwesomeAssertions;
using EwaysVNext.Core.Implementation.FileTypeDetection;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using EwaysVNext.Modules.Customer.Application.Service;
using EwaysVNext.Modules.Customer.Application.Service.CompanyVerification;
using EwaysVNext.Modules.Customer.Application.Service.CompanyVerification.Abstractions;
using EwaysVNext.Modules.Location.Contracts.Abstractions;
using EwaysVNext.Modules.Location.Contracts.Locations;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using System.Text;
using Xunit;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;
using TeamMemberAggregate = EwaysVNext.Modules.Customer.Application.Domain.TeamMembers.TeamMember;

namespace EwaysVNext.UnitTests;

/// <summary>
/// Application-layer orchestration for the Legal Info feature: location validation, document
/// validation, tax-authority verification gating, and the expert-review workflow. The external
/// tax-authority verification is faked here per the story's own testing instruction - it must
/// never depend on a real external provider.
/// </summary>
public class LegalInformationServiceTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();
    private const int ProvinceId = 1;
    private const int NonTehranCityId = 10;
    private const int TehranCityId = 20;
    private const int TehranDistrictId = 200;

    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly ITeamMemberRepository _teamMembers = Substitute.For<ITeamMemberRepository>();
    private readonly ILocationService _locationService = Substitute.For<ILocationService>();
    private readonly ILegalInformationDocumentStorage _storage = Substitute.For<ILegalInformationDocumentStorage>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FixedClock _clock = new();

    private sealed class FakeCompanyVerificationService : ICompanyVerificationService
    {
        public Result NextResult { get; set; } = Result.Success();
        public CompanyVerificationRequest? LastRequest { get; private set; }

        public Task<Result> VerifyAsync(CompanyVerificationRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(NextResult);
        }
    }

    private readonly FakeCompanyVerificationService _verification = new();

    public LegalInformationServiceTests()
    {
        _locationService.CreateAddressAsync(
                Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Success(new AddressResponse(
                IdGenerator.Generate(), ProvinceId, (int)ci[0], (int?)ci[1], (string?)ci[2], (string?)ci[3], (string?)ci[4], (string?)ci[5], (bool)ci[6], 1, TestTime.Now.UtcDateTime, null)));

        _locationService.RemovePrimaryAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    private LegalInformationService CreateSut() => new(
        _customers, _teamMembers, _locationService, _verification, _storage, new FileTypeDetector(), _unitOfWork, _clock, IdGenerator,
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Customer:LegalInformation:TehranCityCode"] = "TEHRAN" })
            .Build());

    private static CustomerAggregate ExistingCustomer() =>
        CustomerAggregate.Register(IdGenerator.Generate(), Guid.NewGuid(), MobileNumber.Create("09123456789"), TestTime.Now);

    private void SetUpCustomer(CustomerAggregate customer) =>
        _customers.GetCustomerWithLegalInformationAsync(customer.IdentityUserId, Arg.Any<CancellationToken>()).Returns(customer);

    private void SetUpNonTehranCity() =>
        _locationService.GetCityById(NonTehranCityId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CitiesResponse(NonTehranCityId, "Shiraz", new List<DistrictResponse>(), ProvinceId, code: "SHIRAZ")));

    private void SetUpTehranCity() =>
        _locationService.GetCityById(TehranCityId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CitiesResponse(
                TehranCityId, "Tehran",
                new List<DistrictResponse> { new(TehranDistrictId, "District 1") },
                ProvinceId, code: "TEHRAN")));

    private static LegalInformationInput ValidInput(int cityId = NonTehranCityId, int? districtId = null) => new(
        "Eways Trading Co.", "12345678901", new DateOnly(2015, 1, 1), "998877",
        "Sara Ahmadi", "09123456789", ProvinceId, cityId, districtId,
        "No. 1, Valiasr St.", "1234567890", "02112345678", null);

    private static (byte[] Bytes, string FileName) ValidPdf() =>
        (Encoding.ASCII.GetBytes("%PDF-1.4 rest of file content padding to be realistic"), "gazette.pdf");

    // ----- GetLegalInformationAsync -----

    [Fact]
    public async Task GetLegalInformationAsync_Returns_NotFound_For_An_Unknown_Customer()
    {
        _customers.GetCustomerWithLegalInformationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((CustomerAggregate?)null);

        var result = await CreateSut().GetLegalInformationAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task GetLegalInformationAsync_Returns_Draft_Status_And_No_Details_Before_Anything_Is_Created()
    {
        var customer = ExistingCustomer();
        SetUpCustomer(customer);

        var result = await CreateSut().GetLegalInformationAsync(customer.IdentityUserId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(LegalInformationStatusType.Draft));
        result.Value.LegalInformation.Should().BeNull();
        result.Value.CanSubmit.Should().BeFalse();
    }

    // ----- SaveLegalInformationDraftAsync / location validation -----

    [Fact]
    public async Task SaveDraftAsync_Persists_A_New_Draft_With_Valid_Data()
    {
        var customer = ExistingCustomer();
        SetUpCustomer(customer);
        SetUpNonTehranCity();

        var result = await CreateSut().SaveLegalInformationDraftAsync(customer.IdentityUserId, ValidInput());

        result.IsSuccess.Should().BeTrue();
        result.Value!.LegalInformation!.CompanyName.Should().Be("Eways Trading Co.");
        customer.LegalInformation.Should().NotBeNull();
        _customers.Received(1).Update(customer);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveDraftAsync_Rejects_An_Unknown_City()
    {
        var customer = ExistingCustomer();
        SetUpCustomer(customer);
        _locationService.GetCityById(999, Arg.Any<CancellationToken>()).Returns(Result.Failure<CitiesResponse>(Error.NotFound));

        var result = await CreateSut().SaveLegalInformationDraftAsync(customer.IdentityUserId, ValidInput(cityId: 999));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_CITY_NOT_FOUND");
    }

    [Fact]
    public async Task SaveDraftAsync_Rejects_When_The_City_Does_Not_Belong_To_The_Selected_Province()
    {
        var customer = ExistingCustomer();
        SetUpCustomer(customer);
        _locationService.GetCityById(NonTehranCityId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CitiesResponse(NonTehranCityId, "Shiraz", new List<DistrictResponse>(), provinceId: 999, code: "SHIRAZ")));

        var result = await CreateSut().SaveLegalInformationDraftAsync(customer.IdentityUserId, ValidInput());

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_PROVINCE_CITY_MISMATCH");
    }

    [Fact]
    public async Task SaveDraftAsync_Rejects_A_District_Supplied_For_A_NonTehran_City()
    {
        var customer = ExistingCustomer();
        SetUpCustomer(customer);
        SetUpNonTehranCity();

        var result = await CreateSut().SaveLegalInformationDraftAsync(customer.IdentityUserId, ValidInput(districtId: 1));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_DISTRICT_NOT_ALLOWED");
    }

    [Fact]
    public async Task SaveDraftAsync_Requires_A_District_When_The_City_Is_Tehran()
    {
        var customer = ExistingCustomer();
        SetUpCustomer(customer);
        SetUpTehranCity();

        var result = await CreateSut().SaveLegalInformationDraftAsync(customer.IdentityUserId, ValidInput(cityId: TehranCityId));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_DISTRICT_REQUIRED_FOR_TEHRAN");
    }

    [Fact]
    public async Task SaveDraftAsync_Rejects_A_District_That_Does_Not_Belong_To_The_Selected_Tehran_City()
    {
        var customer = ExistingCustomer();
        SetUpCustomer(customer);
        SetUpTehranCity();

        var result = await CreateSut().SaveLegalInformationDraftAsync(customer.IdentityUserId, ValidInput(cityId: TehranCityId, districtId: 999));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_DISTRICT_NOT_FOUND");
    }

    [Fact]
    public async Task SaveDraftAsync_Accepts_A_Valid_Tehran_District()
    {
        var customer = ExistingCustomer();
        SetUpCustomer(customer);
        SetUpTehranCity();

        var result = await CreateSut().SaveLegalInformationDraftAsync(customer.IdentityUserId, ValidInput(cityId: TehranCityId, districtId: TehranDistrictId));

        result.IsSuccess.Should().BeTrue();
    }

    // ----- UploadLegalInformationDocumentAsync -----

    [Fact]
    public async Task UploadDocumentAsync_Requires_A_Draft_To_Already_Exist()
    {
        var customer = ExistingCustomer();
        SetUpCustomer(customer);
        var (bytes, name) = ValidPdf();

        var result = await CreateSut().UploadLegalInformationDocumentAsync(
            customer.IdentityUserId, LegalInformationDocumentType.OfficialGazette, new MemoryStream(bytes), name, "application/pdf", bytes.Length);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_NOT_STARTED");
    }

    [Fact]
    public async Task UploadDocumentAsync_Rejects_A_Disallowed_Extension()
    {
        var customer = ExistingCustomer();
        customer.SetLegalInformation(LegalInformation.CreateDraft(IdGenerator.Generate(), customer.Id, TestTime.Now), TestTime.Now);
        SetUpCustomer(customer);
        var bytes = Encoding.ASCII.GetBytes("plain text content");

        var result = await CreateSut().UploadLegalInformationDocumentAsync(
            customer.IdentityUserId, LegalInformationDocumentType.OfficialGazette, new MemoryStream(bytes), "gazette.txt", "text/plain", bytes.Length);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_DOCUMENT_EXTENSION_NOT_ALLOWED");
    }

    [Fact]
    public async Task UploadDocumentAsync_Rejects_A_File_Larger_Than_Five_Megabytes()
    {
        var customer = ExistingCustomer();
        customer.SetLegalInformation(LegalInformation.CreateDraft(IdGenerator.Generate(), customer.Id, TestTime.Now), TestTime.Now);
        SetUpCustomer(customer);
        var (bytes, name) = ValidPdf();
        var tooLarge = LegalInformationDocumentValidator.MaxSizeBytes + 1;

        var result = await CreateSut().UploadLegalInformationDocumentAsync(
            customer.IdentityUserId, LegalInformationDocumentType.OfficialGazette, new MemoryStream(bytes), name, "application/pdf", tooLarge);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_DOCUMENT_TOO_LARGE");
    }

    [Fact]
    public async Task UploadDocumentAsync_Rejects_Content_That_Does_Not_Match_An_Allowed_Extension()
    {
        var customer = ExistingCustomer();
        customer.SetLegalInformation(LegalInformation.CreateDraft(IdGenerator.Generate(), customer.Id, TestTime.Now), TestTime.Now);
        SetUpCustomer(customer);
        var bytes = Encoding.ASCII.GetBytes("this is not really a pdf");

        var result = await CreateSut().UploadLegalInformationDocumentAsync(
            customer.IdentityUserId, LegalInformationDocumentType.OfficialGazette, new MemoryStream(bytes), "gazette.pdf", "application/pdf", bytes.Length);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_DOCUMENT_CONTENT_INVALID");
    }

    [Fact]
    public async Task UploadDocumentAsync_Accepts_A_Valid_Pdf_And_Persists_Its_Metadata()
    {
        var customer = ExistingCustomer();
        customer.SetLegalInformation(LegalInformation.CreateDraft(IdGenerator.Generate(), customer.Id, TestTime.Now), TestTime.Now);
        SetUpCustomer(customer);
        var (bytes, name) = ValidPdf();
        _storage.SaveAsync(customer.Id, Arg.Any<long>(), LegalInformationDocumentType.OfficialGazette, Arg.Any<Stream>(), name, Arg.Any<CancellationToken>())
            .Returns("customer/1/gazette.pdf");

        var result = await CreateSut().UploadLegalInformationDocumentAsync(
            customer.IdentityUserId, LegalInformationDocumentType.OfficialGazette, new MemoryStream(bytes), name, "application/pdf", bytes.Length);

        result.IsSuccess.Should().BeTrue();
        customer.LegalInformation!.Documents.Should().ContainSingle(d => d.FileName == name);
    }

    [Theory]
    [InlineData("letter.jpg", "image/jpeg")]
    [InlineData("letter.JPEG", "image/jpeg")]
    [InlineData("letter.png", "image/png")]
    public async Task UploadDocumentAsync_Accepts_Allowed_Images(string fileName, string expectedContentType)
    {
        var customer = ExistingCustomer();
        customer.SetLegalInformation(LegalInformation.CreateDraft(IdGenerator.Generate(), customer.Id, TestTime.Now), TestTime.Now);
        SetUpCustomer(customer);
        var bytes = expectedContentType == "image/png" ? FileSamples.Png : FileSamples.Jpeg;
        _storage.SaveAsync(default, default, default, default!, default!, default).ReturnsForAnyArgs("customer/1/document");

        var result = await CreateSut().UploadLegalInformationDocumentAsync(
            customer.IdentityUserId, LegalInformationDocumentType.IntroductionLetter, new MemoryStream(bytes), fileName, "application/octet-stream", bytes.Length);

        result.IsSuccess.Should().BeTrue();
        customer.LegalInformation!.Documents.Should().ContainSingle(d => d.ContentType == expectedContentType);
    }

    [Fact]
    public async Task UploadDocumentAsync_Persists_The_Detected_Content_Type_Not_The_Declared_One()
    {
        var customer = ExistingCustomer();
        customer.SetLegalInformation(LegalInformation.CreateDraft(IdGenerator.Generate(), customer.Id, TestTime.Now), TestTime.Now);
        SetUpCustomer(customer);
        var bytes = FileSamples.Png;
        _storage.SaveAsync(default, default, default, default!, default!, default).ReturnsForAnyArgs("customer/1/document");

        var result = await CreateSut().UploadLegalInformationDocumentAsync(
            customer.IdentityUserId, LegalInformationDocumentType.OfficialGazette, new MemoryStream(bytes), "gazette.pdf", "application/pdf", bytes.Length);

        result.IsSuccess.Should().BeTrue();
        customer.LegalInformation!.Documents.Should().ContainSingle(d => d.ContentType == "image/png");
    }

    [Theory]
    [InlineData("executable")]
    [InlineData("docx")]
    [InlineData("zip")]
    public async Task UploadDocumentAsync_Rejects_Other_Content_Renamed_To_Pdf(string actual)
    {
        var customer = ExistingCustomer();
        customer.SetLegalInformation(LegalInformation.CreateDraft(IdGenerator.Generate(), customer.Id, TestTime.Now), TestTime.Now);
        SetUpCustomer(customer);
        var bytes = actual switch
        {
            "executable" => FileSamples.Executable,
            "docx" => FileSamples.Docx,
            _ => FileSamples.PlainZip,
        };

        var result = await CreateSut().UploadLegalInformationDocumentAsync(
            customer.IdentityUserId, LegalInformationDocumentType.OfficialGazette, new MemoryStream(bytes), "document.pdf", "application/pdf", bytes.Length);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_DOCUMENT_CONTENT_INVALID");
        await _storage.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default, default!, default!, default);
    }

    [Fact]
    public async Task UploadDocumentAsync_Leaves_The_Stream_At_The_Start_For_Storage()
    {
        var customer = ExistingCustomer();
        customer.SetLegalInformation(LegalInformation.CreateDraft(IdGenerator.Generate(), customer.Id, TestTime.Now), TestTime.Now);
        SetUpCustomer(customer);
        var (bytes, name) = ValidPdf();
        long? positionSeenByStorage = null;
        _storage.SaveAsync(customer.Id, Arg.Any<long>(), Arg.Any<LegalInformationDocumentType>(), Arg.Do<Stream>(s => positionSeenByStorage = s.Position), name, Arg.Any<CancellationToken>())
            .Returns("key");

        await CreateSut().UploadLegalInformationDocumentAsync(
            customer.IdentityUserId, LegalInformationDocumentType.OfficialGazette, new MemoryStream(bytes), name, "application/pdf", bytes.Length);

        positionSeenByStorage.Should().Be(0);
    }

    // ----- SubmitLegalInformationAsync -----

    private CustomerAggregate CustomerWithCompleteDraft()
    {
        var customer = ExistingCustomer();
        var legalInfo = LegalInformation.CreateDraft(IdGenerator.Generate(), customer.Id, TestTime.Now);
        legalInfo.UpdateDetails(
            "Eways Trading Co.", "12345678901", new DateOnly(2015, 1, 1), "998877",
            "Sara Ahmadi", "09123456789", ProvinceId, NonTehranCityId, null,
            "No. 1, Valiasr St.", "1234567890", "02112345678", null,
            NonTehranCityId, null, "No. 1, Valiasr St.", "1234567890", TestTime.Now);
        legalInfo.AttachDocument(IdGenerator.Generate(), LegalInformationDocumentType.IntroductionLetter, "k1", "letter.pdf", "application/pdf", 10, TestTime.Now);
        legalInfo.AttachDocument(IdGenerator.Generate(), LegalInformationDocumentType.OfficialGazette, "k2", "gazette.pdf", "application/pdf", 10, TestTime.Now);
        customer.SetLegalInformation(legalInfo, TestTime.Now);

        var addressId = IdGenerator.Generate();
        customer.AddAddress(CustomerAddress.Create(IdGenerator.Generate(), customer.Id, addressId));
        _locationService.GetPrimaryAddressAsync(Arg.Any<List<long>>(), Arg.Any<CancellationToken>())
            .Returns(new AddressResponse(addressId, ProvinceId, NonTehranCityId, null, "No. 1, Valiasr St.", "1234567890", null, null, true, 1, TestTime.Now.UtcDateTime, null));

        return customer;
    }

    [Fact]
    public async Task SubmitAsync_Fails_And_Leaves_The_Draft_Unchanged_When_Tax_Verification_Is_Unavailable()
    {
        var customer = CustomerWithCompleteDraft();
        SetUpCustomer(customer);
        _verification.NextResult = Result.Failure(new Error(ItHubCompanyVerificationService.UnavailableErrorCode, "unavailable", 503));

        var result = await CreateSut().SubmitLegalInformationAsync(customer.IdentityUserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ItHubCompanyVerificationService.UnavailableErrorCode);
        customer.LegalInformation!.Status.Should().Be(LegalInformationStatusType.Draft, "a failed verification must not transition the request to Awaiting Review");
        _customers.DidNotReceive().Update(Arg.Any<CustomerAggregate>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_Fails_With_A_Safe_Message_When_Tax_Verification_Does_Not_Match()
    {
        var customer = CustomerWithCompleteDraft();
        SetUpCustomer(customer);
        _verification.NextResult = Result.Failure(new Error("PROVIDER_SPECIFIC_CODE", "some internal provider detail", 400));

        var result = await CreateSut().SubmitLegalInformationAsync(customer.IdentityUserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_TAX_VERIFICATION_FAILED");
        result.Error.Message.Should().NotContain("provider detail", "provider-specific details must never reach the client");
        customer.LegalInformation!.Status.Should().Be(LegalInformationStatusType.Draft);
    }

    [Fact]
    public async Task SubmitAsync_Succeeds_And_Transitions_To_AwaitingReview_When_Verification_Passes()
    {
        var customer = CustomerWithCompleteDraft();
        SetUpCustomer(customer);
        _verification.NextResult = Result.Success();

        var result = await CreateSut().SubmitLegalInformationAsync(customer.IdentityUserId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(LegalInformationStatusType.AwaitingReview));
        customer.LegalInformation!.Status.Should().Be(LegalInformationStatusType.AwaitingReview);
        _verification.LastRequest.Should().NotBeNull();
        _verification.LastRequest!.NationalId.Should().Be("12345678901");
        _customers.Received(1).Update(customer);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_Fails_When_Required_Fields_Are_Missing()
    {
        var customer = ExistingCustomer();
        customer.SetLegalInformation(LegalInformation.CreateDraft(IdGenerator.Generate(), customer.Id, TestTime.Now), TestTime.Now);
        SetUpCustomer(customer);

        var result = await CreateSut().SubmitLegalInformationAsync(customer.IdentityUserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().StartWith("LEGALINFO_").And.EndWith("_REQUIRED");
    }

    [Fact]
    public async Task SubmitAsync_Cannot_Be_Called_Twice_While_Already_Awaiting_Review()
    {
        var customer = CustomerWithCompleteDraft();
        SetUpCustomer(customer);
        _verification.NextResult = Result.Success();
        var sut = CreateSut();
        (await sut.SubmitLegalInformationAsync(customer.IdentityUserId)).IsSuccess.Should().BeTrue();

        var result = await sut.SubmitLegalInformationAsync(customer.IdentityUserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_ALREADY_SUBMITTED");
    }

    // ----- Approve / Reject -----

    private void SetUpReviewer(TeamMemberAggregate reviewer) =>
        _teamMembers.GetByIdentityUserIdAsync(reviewer.IdentityUserId, Arg.Any<CancellationToken>()).Returns(reviewer);

    private static TeamMemberAggregate ExistingReviewer() =>
        TeamMemberAggregate.Create(
            IdGenerator.Generate(), "Ali", "Ahmadi", Gender.Male, NationalCode.Create("0012345679"),
            new DateOnly(1990, 1, 1), MobileNumber.Create("09121110000"), "PC-100", "Support", "Agent",
            Guid.NewGuid(), TestTime.Now);

    private CustomerAggregate CustomerAwaitingReview()
    {
        var customer = CustomerWithCompleteDraft();
        customer.LegalInformation!.Submit(TestTime.Now);
        return customer;
    }

    [Fact]
    public async Task ApproveAsync_Fails_When_The_Reviewer_Cannot_Be_Identified()
    {
        var customer = CustomerAwaitingReview();
        _customers.GetByIdWithLegalInformationAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        _teamMembers.GetByIdentityUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TeamMemberAggregate?)null);

        var result = await CreateSut().ApproveLegalInformationAsync(customer.Id, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("REVIEWING_TEAMMEMBER_NOT_FOUND");
    }

    [Fact]
    public async Task ApproveAsync_Approves_And_Persists()
    {
        var customer = CustomerAwaitingReview();
        _customers.GetByIdWithLegalInformationAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var reviewer = ExistingReviewer();
        SetUpReviewer(reviewer);

        var result = await CreateSut().ApproveLegalInformationAsync(customer.Id, reviewer.IdentityUserId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(LegalInformationStatusType.Approved));
        customer.LegalInformation!.ReviewerId.Should().Be(reviewer.Id);
        _customers.Received(1).Update(customer);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectAsync_Requires_A_Non_Empty_Reason()
    {
        var customer = CustomerAwaitingReview();
        _customers.GetByIdWithLegalInformationAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var reviewer = ExistingReviewer();
        SetUpReviewer(reviewer);

        var result = await CreateSut().RejectLegalInformationAsync(
            customer.Id, reviewer.IdentityUserId, "  ", LegalInformationEditableField.CompanyName);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LEGALINFO_REJECTION_REASON_REQUIRED");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectAsync_Sets_The_Rejection_Reason_And_The_Editable_Fields()
    {
        var customer = CustomerAwaitingReview();
        _customers.GetByIdWithLegalInformationAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        var reviewer = ExistingReviewer();
        SetUpReviewer(reviewer);

        var result = await CreateSut().RejectLegalInformationAsync(
            customer.Id, reviewer.IdentityUserId, "Company name mismatch.",
            LegalInformationEditableField.CompanyName | LegalInformationEditableField.RegistrationNumber);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(LegalInformationStatusType.Rejected));
        result.Value.RejectionReason.Should().Be("Company name mismatch.");
        result.Value.EditableFields.Should().BeEquivalentTo(["CompanyName", "RegistrationNumber"]);
    }

    [Fact]
    public async Task Full_Cycle_Resubmission_After_Correcting_Only_The_Flagged_Field_Succeeds()
    {
        var customer = CustomerAwaitingReview();
        var reviewer = ExistingReviewer();
        SetUpReviewer(reviewer);
        _customers.GetByIdWithLegalInformationAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        SetUpCustomer(customer);
        SetUpNonTehranCity();
        var sut = CreateSut();

        await sut.RejectLegalInformationAsync(customer.Id, reviewer.IdentityUserId, "Bad company name.", LegalInformationEditableField.CompanyName);

        var updated = ValidInput() with { CompanyName = "Corrected Co." };
        var updateResult = await sut.SaveLegalInformationDraftAsync(customer.IdentityUserId, updated);
        updateResult.IsSuccess.Should().BeTrue();

        _verification.NextResult = Result.Success();
        var submitResult = await sut.SubmitLegalInformationAsync(customer.IdentityUserId);

        submitResult.IsSuccess.Should().BeTrue();
        submitResult.Value!.Status.Should().Be(nameof(LegalInformationStatusType.AwaitingReview));
        submitResult.Value.LegalInformation!.CompanyName.Should().Be("Corrected Co.");
    }
}
