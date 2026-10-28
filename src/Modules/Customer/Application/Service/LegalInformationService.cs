using System.Buffers;
using EwaysVNext.Core.Application.FileTypeDetection;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using EwaysVNext.Modules.Customer.Application.Service.CompanyVerification.Abstractions;
using EwaysVNext.Modules.Location.Contracts.Abstractions;
using EwaysVNext.Modules.Location.Contracts.Locations;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using Microsoft.Extensions.Configuration;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;

namespace EwaysVNext.Modules.Customer.Application.Service;

public sealed class LegalInformationService : ILegalInformationInboundPort
{
    private readonly ICustomerRepository _customers;
    private readonly ITeamMemberRepository _teamMembers;
    private readonly ILocationService _locationService;
    private readonly ICompanyVerificationService _companyVerificationService;
    private readonly ILegalInformationDocumentStorage _documentStorage;
    private readonly IFileTypeDetector _fileTypeDetector;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;

    private readonly string _tehranCityCode;

    public LegalInformationService(
        ICustomerRepository customers,
        ITeamMemberRepository teamMembers,
        ILocationService locationService,
        ICompanyVerificationService companyVerificationService,
        ILegalInformationDocumentStorage documentStorage,
        IFileTypeDetector fileTypeDetector,
        IUnitOfWork unitOfWork,
        IClock clock,
        IIdGenerator idGenerator,
        IConfiguration configuration)
    {
        _customers = customers;
        _teamMembers = teamMembers;
        _locationService = locationService;
        _companyVerificationService = companyVerificationService;
        _documentStorage = documentStorage;
        _fileTypeDetector = fileTypeDetector;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _idGenerator = idGenerator;
        _tehranCityCode = configuration["Customer:LegalInformation:TehranCityCode"];
    }

    private static readonly Error CustomerNotFound = new("CUSTOMER_NOT_FOUND", "Customer was not found.");
    private static readonly Error ReviewingTeamMemberNotFound =
        new("REVIEWING_TEAMMEMBER_NOT_FOUND", "The reviewing team member could not be identified.");
    private static readonly Error LegalInformationNotStarted =
        new("LEGALINFO_NOT_STARTED", "No legal information has been created yet.", 404);
    private static readonly Error CityNotFound = new("LEGALINFO_CITY_NOT_FOUND", "The specified city was not found.");
    private static readonly Error ProvinceCityMismatch =
        new("LEGALINFO_PROVINCE_CITY_MISMATCH", "The selected city does not belong to the selected province.");
    private static readonly Error DistrictNotFound = new("LEGALINFO_DISTRICT_NOT_FOUND", "The specified district was not found for this city.");
    private static readonly Error DistrictRequiredForTehran =
        new("LEGALINFO_DISTRICT_REQUIRED_FOR_TEHRAN", "A municipality district is required when the city is Tehran.");
    private static readonly Error DistrictNotAllowedForNonTehran =
        new("LEGALINFO_DISTRICT_NOT_ALLOWED", "A district can only be supplied when the city is Tehran.");
    private static readonly Error TaxVerificationFailed = new(
        "LEGALINFO_TAX_VERIFICATION_FAILED",
        "The submitted company details do not match the tax authority's records.",
        400);

    public async Task<Result<LegalInformationResponse>> GetLegalInformationAsync(Guid identityUserId, CancellationToken ct = default)
    {
        var customer = await _customers.GetCustomerWithLegalInformationAsync(identityUserId, ct);
        if (customer is null)
            return Result.Failure<LegalInformationResponse>(CustomerNotFound);

        var address = await GetPrimaryAddressAsync(customer, ct);
        return Result.Success(ToResponse(customer.LegalInformation, address));
    }

    public async Task<Result<LegalInformationResponse>> SaveLegalInformationDraftAsync(
        Guid identityUserId,
        LegalInformationInput input,
        CancellationToken ct = default)
    {
        var customer = await _customers.GetCustomerWithLegalInformationAsync(identityUserId, ct);
        if (customer is null)
            return Result.Failure<LegalInformationResponse>(CustomerNotFound);

        var locationCheck = await ValidateLocationAsync(input.ProvinceId, input.CityId, input.DistrictId, ct);
        if (locationCheck.IsFailure)
            return Result.Failure<LegalInformationResponse>(locationCheck.Error!);

        var now = _clock.UtcNow;

        var legalInfo = customer.LegalInformation;
        if (legalInfo is null)
        {
            legalInfo = LegalInformation.CreateDraft(_idGenerator.Generate(), customer.Id, now);
            customer.SetLegalInformation(legalInfo, now);
        }

        var currentPrimaryAddress = await GetPrimaryAddressAsync(customer, ct);

        try
        {
            legalInfo.UpdateDetails(
                input.CompanyName,
                input.NationalId,
                input.RegistrationDate,
                input.RegistrationNumber,
                input.CeoName,
                input.CeoMobileNumber,
                input.ProvinceId,
                input.CityId,
                input.DistrictId,
                input.Address,
                input.PostalCode,
                input.LandlineNumber,
                input.Fax,
                currentPrimaryAddress?.cityId ?? 0,
                currentPrimaryAddress?.districtId,
                currentPrimaryAddress?.addressText,
                currentPrimaryAddress?.postalCode,
                now);
        }
        catch (DomainException dex)
        {
            return Result.Failure<LegalInformationResponse>(new Error(dex.Code, dex.Message));
        }

        if (currentPrimaryAddress is not null)
        {
            var demoted = await _locationService.RemovePrimaryAsync(currentPrimaryAddress.id, ct);
            if (demoted.IsFailure)
                return Result.Failure<LegalInformationResponse>(demoted.Error!);
        }

        var created = await _locationService.CreateAddressAsync(
            input.CityId, input.DistrictId, input.Address, input.PostalCode,
            recipientName: null, recipientMobile: null, isPrimary: true, ct);
        if (created.IsFailure)
            return Result.Failure<LegalInformationResponse>(created.Error!);

        customer.AddAddress(CustomerAddress.Create(_idGenerator.Generate(), customer.Id, created.Value.id));

        _customers.Update(customer);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToResponse(legalInfo, created.Value));
    }

    public async Task<Result<LegalInformationResponse>> UploadLegalInformationDocumentAsync(
        Guid identityUserId,
        LegalInformationDocumentType type,
        Stream content,
        string fileName,
        string declaredContentType,
        long sizeBytes,
        CancellationToken ct = default)
    {
        var customer = await _customers.GetCustomerWithLegalInformationAsync(identityUserId, ct);
        if (customer is null)
            return Result.Failure<LegalInformationResponse>(CustomerNotFound);

        if (customer.LegalInformation is null)
            return Result.Failure<LegalInformationResponse>(LegalInformationNotStarted);

        var detected = await DetectFileTypeAsync(content, ct);

        var validation = LegalInformationDocumentValidator.Validate(fileName, sizeBytes, detected);
        if (validation.IsFailure)
            return Result.Failure<LegalInformationResponse>(validation.Error!);

        // The detected type, never the client-declared one, is what gets persisted.
        var detectedContentType = detected.MimeType!;
        var now = _clock.UtcNow;
        var documentId = _idGenerator.Generate();

        var storageKey = await _documentStorage.SaveAsync(customer.Id, documentId, type, content, fileName, ct);

        try
        {
            customer.LegalInformation.AttachDocument(documentId, type, storageKey, fileName, detectedContentType, sizeBytes, now);
        }
        catch (DomainException dex)
        {
            return Result.Failure<LegalInformationResponse>(new Error(dex.Code, dex.Message));
        }

        _customers.Update(customer);
        await _unitOfWork.SaveChangesAsync(ct);

        var address = await GetPrimaryAddressAsync(customer, ct);
        return Result.Success(ToResponse(customer.LegalInformation, address));
    }

    public async Task<Result<LegalInformationResponse>> SubmitLegalInformationAsync(Guid identityUserId, CancellationToken ct = default)
    {
        var customer = await _customers.GetCustomerWithLegalInformationAsync(identityUserId, ct);
        if (customer is null)
            return Result.Failure<LegalInformationResponse>(CustomerNotFound);

        var legalInfo = customer.LegalInformation;
        if (legalInfo is null)
            return Result.Failure<LegalInformationResponse>(LegalInformationNotStarted);

        if (!legalInfo.CanSubmit)
            return Result.Failure<LegalInformationResponse>(new Error(
                "LEGALINFO_ALREADY_SUBMITTED",
                $"This legal information is already {legalInfo.Status} and cannot be submitted again.",
                409));

        var now = _clock.UtcNow;

        try
        {
            // Validates completeness only - no state is mutated yet.
            legalInfo.EnsureReadyToSubmit();
        }
        catch (DomainException dex)
        {
            return Result.Failure<LegalInformationResponse>(new Error(dex.Code, dex.Message));
        }

        var primaryAddress = await GetPrimaryAddressAsync(customer, ct);
        if (primaryAddress is null)
            return Result.Failure<LegalInformationResponse>(new Error("LEGALINFO_ADDRESS_REQUIRED", "Address is required."));

        var verification = await _companyVerificationService.VerifyAsync(
            new CompanyVerificationRequest(legalInfo.CompanyName, legalInfo.NationalId, legalInfo.RegistrationDate, legalInfo.RegistrationNumber, legalInfo.CeoName),
            ct);

        if (verification.IsFailure)
        {
            var error = verification.Error!.Code == CompanyVerification.ItHubCompanyVerificationService.UnavailableErrorCode
                ? verification.Error
                : TaxVerificationFailed;
            return Result.Failure<LegalInformationResponse>(error);
        }

        legalInfo.Submit(now);
        _customers.Update(customer);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToResponse(legalInfo, primaryAddress));
    }

    public async Task<Result<LegalInformationResponse>> ApproveLegalInformationAsync(
        long customerId,
        Guid reviewingIdentityUserId,
        CancellationToken ct = default)
    {
        var customer = await _customers.GetByIdWithLegalInformationAsync(customerId, ct);
        if (customer is null)
            return Result.Failure<LegalInformationResponse>(CustomerNotFound);

        if (customer.LegalInformation is null)
            return Result.Failure<LegalInformationResponse>(LegalInformationNotStarted);

        var reviewer = await _teamMembers.GetByIdentityUserIdAsync(reviewingIdentityUserId, ct);
        if (reviewer is null)
            return Result.Failure<LegalInformationResponse>(ReviewingTeamMemberNotFound);

        var now = _clock.UtcNow;

        try
        {
            customer.LegalInformation.Approve(reviewer.Id, now);
        }
        catch (DomainException dex)
        {
            return Result.Failure<LegalInformationResponse>(new Error(dex.Code, dex.Message));
        }

        _customers.Update(customer);
        await _unitOfWork.SaveChangesAsync(ct);

        var address = await GetPrimaryAddressAsync(customer, ct);
        return Result.Success(ToResponse(customer.LegalInformation, address));
    }

    public async Task<Result<LegalInformationResponse>> RejectLegalInformationAsync(
        long customerId,
        Guid reviewingIdentityUserId,
        string reason,
        LegalInformationEditableField editableFields,
        CancellationToken ct = default)
    {
        var customer = await _customers.GetByIdWithLegalInformationAsync(customerId, ct);
        if (customer is null)
            return Result.Failure<LegalInformationResponse>(CustomerNotFound);

        if (customer.LegalInformation is null)
            return Result.Failure<LegalInformationResponse>(LegalInformationNotStarted);

        var reviewer = await _teamMembers.GetByIdentityUserIdAsync(reviewingIdentityUserId, ct);
        if (reviewer is null)
            return Result.Failure<LegalInformationResponse>(ReviewingTeamMemberNotFound);

        var now = _clock.UtcNow;

        try
        {
            customer.LegalInformation.Reject(reviewer.Id, reason, editableFields, now);
        }
        catch (DomainException dex)
        {
            return Result.Failure<LegalInformationResponse>(new Error(dex.Code, dex.Message));
        }

        _customers.Update(customer);
        await _unitOfWork.SaveChangesAsync(ct);

        var address = await GetPrimaryAddressAsync(customer, ct);
        return Result.Success(ToResponse(customer.LegalInformation, address));
    }

    private Task<AddressResponse?> GetPrimaryAddressAsync(CustomerAggregate customer, CancellationToken ct)
    {
        var addressIds = customer.GetAddresses().Select(a => a.AddressId).ToList();
        return _locationService.GetPrimaryAddressAsync(addressIds, ct);
    }

    private async Task<Result> ValidateLocationAsync(int provinceId, int cityId, int? districtId, CancellationToken ct)
    {
        var cityLookup = await _locationService.GetCityById(cityId, ct);
        if (cityLookup.IsFailure)
            return Result.Failure(CityNotFound);

        var city = cityLookup.Value!;
        if (city.provinceId != provinceId)
            return Result.Failure(ProvinceCityMismatch);

        var isTehran = string.Equals(city.code, _tehranCityCode, StringComparison.OrdinalIgnoreCase);

        if (isTehran && districtId is null)
            return Result.Failure(DistrictRequiredForTehran);

        if (!isTehran && districtId is not null)
            return Result.Failure(DistrictNotAllowedForNonTehran);

        if (districtId is not null && city.GetDistrictById(districtId.Value) is null)
            return Result.Failure(DistrictNotFound);

        return Result.Success();
    }

    private async Task<FileTypeDetectionResult> DetectFileTypeAsync(Stream content, CancellationToken ct)
    {
        var length = _fileTypeDetector.HeaderLength;
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            if (content.CanSeek)
                content.Position = 0;

            var total = 0;
            while (total < length)
            {
                var read = await content.ReadAsync(buffer.AsMemory(total, length - total), ct);
                if (read == 0)
                    break;
                total += read;
            }

            if (content.CanSeek)
                content.Position = 0;

            return _fileTypeDetector.Detect(buffer.AsSpan(0, total));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static LegalInformationResponse ToResponse(LegalInformation? legalInfo, AddressResponse? address)
    {
        if (legalInfo is null)
        {
            return new LegalInformationResponse(
                LegalInformationStatusType.Draft.ToString(),
                null,
                Array.Empty<LegalInformationDocumentInfo>(),
                null,
                Array.Empty<string>(),
                false);
        }

        var editableFields = Enum.GetValues<LegalInformationEditableField>()
            .Where(f => f != LegalInformationEditableField.None && legalInfo.IsFieldEditable(f))
            .Select(f => f.ToString())
            .ToArray();

        var documents = legalInfo.Documents
            .Select(d => new LegalInformationDocumentInfo(
                ((LegalInformationDocumentType)d.Type).ToString(),
                d.FileName,
                d.ContentType,
                d.SizeBytes))
            .ToArray();

        return new LegalInformationResponse(
            legalInfo.Status.ToString(),
            new LegalInformationDetails(
                legalInfo.CompanyName,
                legalInfo.NationalId,
                legalInfo.RegistrationDate,
                legalInfo.RegistrationNumber,
                legalInfo.CeoName,
                legalInfo.CeoMobileNumber,
                address?.provinceId??0,
                address?.cityId ?? 0,
                address?.districtId,
                address?.addressText ?? string.Empty,
                address?.postalCode ?? string.Empty,
                legalInfo.LandlineNumber,
                legalInfo.Fax),
            documents,
            legalInfo.RejectionReason,
            editableFields,
            legalInfo.CanSubmit);
    }
}
