using Customer.Infrastructure.Shahkar.DTOs;
using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using EwaysVNext.Modules.Customer.Application.Service.Shahkar;
using EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;
using EwaysVNext.Modules.Location.Contracts.Abstractions;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;
using EwaysVNext.Modules.Customer.Application.Domain.TeamMembers;

namespace EwaysVNext.Modules.Customer.Application.Service;

public sealed class CustomerService(
    ICustomerRepository customers,
    ITeamMemberRepository teamMembers,
    IUnitOfWork unitOfWork,
    IShahkarService shahkarService,
    ILogger<CustomerService> logger,
    ILocationService locationService,
    IClock clock,
    IIdGenerator idGenerator) : Ports.Inbound.ICustomerInboundPort
{
    private static readonly Error CustomerNotFound = new(CustomerErrorCodes.CustomerNotFound, CustomerMessages.CustomerNotFound);
    private static readonly Error ShahkarFailed = new("SHAHKAR_FAILED_GET_DATA", CustomerServiceMessages.ShahkarFailed);
    private static readonly Error ShahkarDoesnMatch = new("SHAHKAR_DOESNT_MATCH", CustomerServiceMessages.ShahkarDoesntMatch);
    private static readonly Error CustomerAlreadyVerifyChecked = new(CustomerErrorCodes.CustomerAlreadyVerifyChecked, CustomerMessages.CustomerAlreadyVerifyChecked);
    private static readonly Error LocationAddressNotValid =
        new(CustomerAddressErrorCodes.LocationAddressNotValid, CustomerAddressMessages.LocationAddressNotValid);
    private static readonly Error ReviewingTeamMemberNotFound =
        new(TeamMemberErrorCodes.ReviewingTeamMemberNotFound, TeamMemberMessages.ReviewingTeamMemberNotFound);
    private static readonly Error RejectionReasonRequired =
        new(CustomerVerificationErrorCodes.RejectionReasonRequired, CustomerVerificationMessages.RejectionReasonRequired);
    private static readonly Error JobTypeNotFound =
        new(CustomerErrorCodes.JobTypeNotFound, CustomerMessages.JobTypeNotFound);
    private static readonly Error InvalidPostalCode =
        new("INVALID_POSTAL_CODE", CustomerServiceMessages.InvalidPostalCode);
    private static readonly Error InvalidCity = new("INVALID_CITY", CustomerAddressServiceMessages.InvalidCity);
    private static readonly Error CityNotInProvince =
        new("CITY_NOT_IN_PROVINCE", CustomerServiceMessages.CityNotInProvince);
    private static readonly Error InvalidDistrict = new("INVALID_DISTRICT", CustomerAddressServiceMessages.InvalidDistrict);
    private static readonly Error AddressTextTooLong =
        new("ADDRESS_TEXT_TOO_LONG", CustomerAddressServiceMessages.AddressTextTooLong);
    private static readonly Regex AdminPostalCodeRegex =
        new("^[0-9]{10}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const int AddressTextMaxLength = 1000;
    private static readonly Error EditNotAllowedWhileAwaitingExpertCall =
        new("CUSTOMER_EDIT_NOT_ALLOWED_WHILE_AWAITING_EXPERT_CALL", CustomerMessages.EditNotAllowedWhileAwaitingExpertCall);


    public async Task<Result<long>> RegisterAsync(Guid identityUserId, string mobileNumber,
    CancellationToken ct = default)
    {
        var existingCustomer = await customers.GetByIdentityUserIdAsync(identityUserId, ct);
        if (existingCustomer is not null)
            return Result.Success(existingCustomer.Id);

        var mobile = MobileNumber.Create(mobileNumber);
        if (await customers.ExistsForMobileNumberAsync(mobile, ct))
            return Result.Success(0L);

        var customer = CustomerAggregate.Register(idGenerator.Generate(), identityUserId, mobile, clock.UtcNow);
        await customers.AddAsync(customer, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(customer.Id);
    }
    #region RegisterCustomerInfoAsync
    public async Task<Result> RegisterCustomerInfoAsync(
        Guid identityUserId,
        string nationalCode,
        string firstName,
        string lastName,
        Gender gender,
        DateOnly birthDate,
        int cityId,
        int? districtId,
        string? address,
        CancellationToken ct = default)
    {
        var customer = await customers.GetCustomerWithVerificationAsync(
            identityUserId,
            ct);

        if (customer is null)
            return Result.Failure<long>(CustomerNotFound);

        if (customer.Verification is not null)
            return Result.Failure<long>(CustomerAlreadyVerifyChecked);

        var location = await locationService.IsValidAddressLocationAsync(cityId, districtId, ct);

        if (location == false)
            return Result.Failure<long>(LocationAddressNotValid);

        var nData = NationalCode.Create(nationalCode);

        nationalCode = nData.Value;

        var now = clock.UtcNow;

        InitializeVerification(
            idGenerator.Generate(),
            customer,
            nationalCode,
            birthDate,
            now);

        var response = await HandleMobileVerificationAsync(
            customer,
            nationalCode,
            firstName,
            lastName,
            gender,
            birthDate,
            now,
            ct);
        await InitializeAddress(customer, cityId,
            districtId,
            address, ct);
        customers.Update(customer);

        await unitOfWork.SaveChangesAsync(ct);

        return response;

    }
    private async Task<Result<VerifyMobileResponse>?> VerifyMobileNationalCodeAsync(
        CustomerAggregate customer,
        string nationalCode,
        CancellationToken ct)
    {
        logger.LogInformation(
            CustomerServiceMessages.MobileVerificationStarted,
            customer.Id);

        var result =
            await shahkarService.VerifyMobileNationalCodeAsync(
                customer.MobileNumber,
                nationalCode,
                ct);

        logger.LogInformation(
            CustomerServiceMessages.MobileVerificationCompleted,
            customer.Id,
            result.IsSuccess,
            result.Value?.Data,
            result.Error?.Code);

        return result;
    }

    private async Task<Result> HandleMobileVerificationAsync(
        CustomerAggregate customer,
        string nationalCode,
        string firstName,
        string lastName,
        Gender gender,
        DateOnly birthDate,
        DateTimeOffset now,
        CancellationToken ct)
    {
        return await VerifyCitizenshipAsync(
            customer,
            nationalCode,
            firstName,
            lastName,
            gender,
            birthDate,
            now,
            ct);
    }

    private async Task<Result> VerifyCitizenshipAsync(
        CustomerAggregate customer,
        string nationalCode,
        string firstName,
        string lastName,
        Gender gender,
        DateOnly birthDate,
        DateTimeOffset now,
        CancellationToken ct)
    {
        logger.LogInformation(
            CustomerServiceMessages.CitizenshipVerificationStarted,
            nationalCode);

        var result =
            await shahkarService.VerifyCitizenshipAsync(
                nationalCode,
                birthDate,
                ct);

        logger.LogInformation(
            CustomerServiceMessages.CitizenshipVerificationCompleted,
            nationalCode,
            result.IsSuccess,
            result.Value?.Data?.Valid,
            result.Value?.Data?.Liveness,
            result.Error?.Code);

        if (IsCitizenshipVerified(result))
        {
            RegisterVerifiedCustomer(
                idGenerator.Generate(),
                customer,
                result,
                nationalCode,
                firstName,
                lastName,
                gender,
                birthDate,
                now);

            return Result.Success(customer.Id);
        }

        if (result?.Value?.Data?.Valid == false)
        {
            return Result.Failure(ShahkarDoesnMatch);
        }

        if (result?.Error?.Code == "ShahkarTimeout")
        {
            return HandlePendingVerification(customer, now);
        }

        return HandlePendingVerification(customer, now);
    }

    private static bool IsCitizenshipVerified(
        Result<ApiResponse<ShahkarIdentityResponse>>? result)
    {
        return result is not null
               && result.IsSuccess
               && result.Value?.Data?.Valid == true
               && result.Value.Data.Liveness == true;
    }

    private static void RegisterVerifiedCustomer(
        long detailsId,
        CustomerAggregate customer,
        Result<ApiResponse<ShahkarIdentityResponse>> result,
        string nationalCode,
        string firstName,
        string lastName,
        Gender gender,
        DateOnly birthDate,
        DateTimeOffset now)
    {
        customer.UpdateVerificationStatus(VerificationStatusType.Verified, now);

        customer.RegisterCustomerInfo(
            NationalCode.Create(nationalCode),
            firstName,
            lastName,
            gender,
            birthDate,
            now);

        var personInfo = result.Value!.Data!.PersonInfo;

        customer.Verification.MarkAsShahkarVerified(
            detailsId,
            personInfo?.FirstName,
            personInfo?.LastName,
            personInfo?.FatherName,
            personInfo?.Gender == "MALE"
                ? Gender.Male
                : Gender.Female,
            result.Value.Data.Liveness,
            now,
            result.Value.Data.Fid);
    }

    private Result HandleMobileVerificationFailed(
        CustomerAggregate customer,
        DateTimeOffset now)
    {
        logger.LogWarning(
            CustomerServiceMessages.MobileVerificationFailed,
            customer.Id);

        customer.Verification.MarkAsNotVerified(null, now);

        customer.UpdateVerificationStatus(VerificationStatusType.NotVerified, now);

        return Result.Failure(ShahkarFailed);
    }

    private Result HandleCitizenshipFailed(
        CustomerAggregate customer,
        string? fid,
        DateTimeOffset now)
    {
        logger.LogWarning(
            CustomerServiceMessages.CitizenshipVerificationFailed,
            customer.Id,
            fid);

        customer.Verification.MarkAsNotVerified(fid, now);

        customer.UpdateVerificationStatus(VerificationStatusType.NotVerified, now);

        return Result.Failure(ShahkarFailed);
    }

    private Result HandlePendingVerification(
        CustomerAggregate customer,
        DateTimeOffset now)
    {
        logger.LogWarning(
            CustomerServiceMessages.VerificationPending,
            customer.Id);

        customer.Verification.MarkAsPendding(now);

        customer.UpdateVerificationStatus(VerificationStatusType.Pending, now);

        return Result.Failure(ShahkarFailed);
    }

    private static void InitializeVerification(
        long verificationId,
        CustomerAggregate customer,
        string nationalCode,
        DateOnly birthDate,
        DateTimeOffset now)
    {
        customer.SetVerification(
            CustomerVerification.Create(
                verificationId,
                customer.Id,
                nationalCode,
                birthDate,
                now),
            now);
    }
    private async Task InitializeAddress(
        CustomerAggregate customer,
        int cityId,
        int? districtId,
        string? addressText, CancellationToken ct)
    {
        if (customer.GetAddresses()?.Any() == false)
        {
            var address = await locationService.CreateAddressAsync(
                cityId: cityId,
                districtId: districtId, addressText: addressText, isPrimary: true, ct: ct);
            if (address.IsSuccess)
            {
                customer.AddAddress(CustomerAddress.Create(idGenerator.Generate(), customer.Id, address.Value.id));
            }
        }
    }
    #endregion

    public async Task<Result> UpdateProfileAsync(Guid identityUserId,
    string firstName,
    string lastName,
    Gender gender,
    DateOnly birthDate,
    CancellationToken ct = default)
    {
        var customer = await customers.GetByIdentityUserIdAsync(identityUserId, ct);
        if (customer == null)
            return Result.Failure<long>(CustomerNotFound);
        customer.Profile(firstName, lastName, gender, birthDate, clock.UtcNow);
        customers.Update(customer);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(customer.Id);
    }

    public async Task<Result<CustomerProfile>> GetProfileAsync(
        Guid identityUserId,
        CancellationToken ct = default)
    {
        var customer = await customers.GetByIdentityUserIdAsync(identityUserId, ct);
        if (customer is null)
            return Result.Failure<CustomerProfile>(CustomerNotFound);

        return Result.Success(new CustomerProfile(
            customer.Id,
            customer.IdentityUserId,
            customer.MobileNumber,
            customer.FirstName,
            customer.LastName,
            customer.NationalCode,
            customer.Gender,
            customer.BirthDate,
            customer.EconomicCode,
            customer.StatusId,
            customer.VerificationStatusId,
            customer.CreatedAt,
            customer.UpdatedAt));
    }

    public async Task<Result> UpdateStatusAsync(
        long customerId,
        CustomerStatusType status,
        Guid reviewingIdentityUserId,
        string? reason = null,
        CancellationToken ct = default)
    {
        var customer = await customers.GetByIdAsync(customerId, ct);
        if (customer is null)
            return Result.Failure(CustomerNotFound);

        if (status == CustomerStatusType.Rejected && string.IsNullOrWhiteSpace(reason))
            return Result.Failure(RejectionReasonRequired);

        //var reviewer = await teamMembers.GetByIdentityUserIdAsync(reviewingIdentityUserId, ct);
        //if (reviewer is null)
        //    return Result.Failure(ReviewingTeamMemberNotFound);

        var now = clock.UtcNow;

        try
        {
            customer.UpdateStatus(status, now);
        }
        catch (DomainException dex)
        {
            return Result.Failure(new Error(dex.Code, dex.Message));
        }

        if (status == CustomerStatusType.Rejected)
            customer.Verification?.RecordRejection(reason!, now);
        else if (status == CustomerStatusType.Approved)
            customer.Verification?.ClearRejectionReason(now);

        customers.Update(customer);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> UpdateCustomerByAdminAsync(
        long customerId,
        AdminCustomerUpdate update,
        CancellationToken ct = default)
    {
        var customer = await customers.GetForAdminUpdateAsync(customerId, ct);
        if (customer is null)
            return Result.Failure(CustomerNotFound);

        if (!CustomerAggregate.CanBeEditedByAdmin(customer.StatusId))
            return Result.Failure(EditNotAllowedWhileAwaitingExpertCall);

        var requestedJobTypeIds = update.JobTypeIds.Distinct().ToArray();
        if (requestedJobTypeIds.Length > 0)
        {
            var existing = await customers.GetExistingJobTypeIdsAsync(requestedJobTypeIds, ct);
            if (existing.Count != requestedJobTypeIds.Length)
                return Result.Failure(JobTypeNotFound);
        }

        AdminCustomerAddressUpdate? address = null;
        if (update.Address is not null)
        {
            var validated = await ValidateAdminAddressAsync(update.Address, ct);
            if (validated.IsFailure)
                return Result.Failure(validated.Error!);
            address = validated.Value;
        }

        var jobTypes = requestedJobTypeIds
            .Select(jobTypeId => (Id: idGenerator.Generate(), JobTypeId: jobTypeId))
            .ToArray();

        try
        {
            customer.UpdateByAdmin(
                update.Gender,
                update.EconomicCode,
                update.IntaCode,
                update.RoleCode,
                string.IsNullOrWhiteSpace(update.Email) ? null : Email.Create(update.Email),
                string.IsNullOrWhiteSpace(update.SecondaryPhoneNumber) ? null : PhoneNumber.Create(update.SecondaryPhoneNumber),
                jobTypes,
                clock.UtcNow);
        }
        catch (DomainException dex)
        {
            return Result.Failure(new Error(dex.Code, dex.Message));
        }

        if (address is not null)
        {
            var saved = await SaveAdminAddressAsync(customer, address, ct);
            if (saved.IsFailure)
                return saved;
        }

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    private async Task<Result<AdminCustomerAddressUpdate>> ValidateAdminAddressAsync(
        AdminCustomerAddressUpdate address, CancellationToken ct)
    {
        var postalCode = string.IsNullOrWhiteSpace(address.PostalCode) ? null : address.PostalCode.Trim();
        if (postalCode is not null && !AdminPostalCodeRegex.IsMatch(postalCode))
            return Result.Failure<AdminCustomerAddressUpdate>(InvalidPostalCode);

        var addressText = string.IsNullOrWhiteSpace(address.AddressText) ? null : address.AddressText.Trim();
        if (addressText is { Length: > AddressTextMaxLength })
            return Result.Failure<AdminCustomerAddressUpdate>(AddressTextTooLong);

        var city = address.CityId > 0 ? await locationService.GetCityById(address.CityId, ct) : null;
        if (city is null || city.IsFailure)
            return Result.Failure<AdminCustomerAddressUpdate>(InvalidCity);
        if (city.Value!.provinceId != address.ProvinceId)
            return Result.Failure<AdminCustomerAddressUpdate>(CityNotInProvince);
        if (address.DistrictId is not null && city.Value.GetDistrictById(address.DistrictId.Value) is null)
            return Result.Failure<AdminCustomerAddressUpdate>(InvalidDistrict);

        return Result.Success(address with { AddressText = addressText, PostalCode = postalCode });
    }

    private async Task<Result> SaveAdminAddressAsync(
        CustomerAggregate customer, AdminCustomerAddressUpdate address, CancellationToken ct)
    {
        var addressIds = customer.Addresses.Select(a => a.AddressId).ToList();
        var primary = addressIds.Count == 0 ? null : await locationService.GetPrimaryAddressAsync(addressIds, ct);

        if (primary is not null)
        {
            var corrected = await locationService.CorrectAddressLocationAsync(
                primary.id, address.CityId, address.DistrictId, address.AddressText, address.PostalCode, ct);
            return corrected.IsSuccess ? Result.Success() : Result.Failure(corrected.Error!);
        }

        var created = await locationService.CreateAddressAsync(
            cityId: address.CityId,
            districtId: address.DistrictId,
            addressText: address.AddressText,
            postalCode: address.PostalCode,
            isPrimary: true,
            ct: ct);
        if (created.IsFailure)
            return Result.Failure(created.Error!);

        customer.AddAddress(CustomerAddress.Create(idGenerator.Generate(), customer.Id, created.Value!.id));
        return Result.Success();
    }

    public async Task<Result> UpdateVerificationStatusAsync(
        long customerId,
        int expertId,
        VerificationStatusType status,
        Guid reviewingIdentityUserId,
        string? reason = null,
        CancellationToken ct = default)
    {
        var customer = await customers.GetByIdAsync(customerId, ct);
        if (customer is null)
            return Result.Failure(CustomerNotFound);

        //var reviewer = await teamMembers.GetByIdentityUserIdAsync(reviewingIdentityUserId, ct);
        //if (reviewer is null)
        //    return Result.Failure(ReviewingTeamMemberNotFound);

        var now = clock.UtcNow;

        try
        {
            customer.UpdateVerificationStatus(status, now, expertId);
        }
        catch (DomainException dex)
        {
            return Result.Failure(new Error(dex.Code, dex.Message));
        }

        customers.Update(customer);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
