using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Location.Application.Domain.Enums;
using EwaysVNext.Modules.Location.Application.Domain.Locations;
using EwaysVNext.Modules.Location.Application.Ports.Outbound;
using EwaysVNext.Modules.Location.Contracts.Abstractions;
using EwaysVNext.Modules.Location.Contracts.Locations;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Location.Application.Service;

public sealed class LocationService(IProvinceRepository provinces,
    IAddressRepository addresses, IUnitOfWork unitOfWork,
    IIdGenerator idGenerator, IClock clock) : ILocationService
{
    private static readonly Error AddressNotFound = new("ADDRESS_NOT_FOUND", LocationServiceMessages.AddressNotFound);
    private static readonly Error PrimaryAddressCannotBeModified =
        new("PRIMARY_ADDRESS_CANNOT_BE_MODIFIED", AddressMessages.PrimaryAddressCannotBeModified);

    public async Task<Result<AddressResponse>> CreateAddressAsync(int cityId,
        int? districtId,
        string? addressText,
        string? postalCode,
        string? recipientName,
        string? recipientMobile,
        bool isPrimary,
        CancellationToken ct)
    {
        var address = Address.Create(idGenerator.Generate(), cityId, clock.UtcNow, districtId, addressText, postalCode, recipientName, recipientMobile, isPrimary);
        await addresses.AddAsync(address, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(await ToResponseAsync(address, ct));
    }
    public async Task<Result<CitiesResponse>> GetCityById(int cityId, CancellationToken ct = default)
    {
        var province = await provinces.GetProvinceAsync(x => x.Cities.Any(x => x.Id == cityId), ct: ct);
        if (province is null)
            return Result.Failure<CitiesResponse>(Error.NotFound);
        var city = province.GetCity(cityId);
        if (city is null)
            return Result.Failure<CitiesResponse>(Error.NotFound);
        return Result.Success(new CitiesResponse(cityId, city.Name, city.Districts?.Select((child) =>
        {
            return new DistrictResponse(child.Id, child.Name);
        })?.ToList(), city.ProvinceId, city.Code));
    }
    public async Task<AddressResponse?> ExistsPrimaryAddressAsync(List<long> ids, CancellationToken ct)
    {
        var address = (await addresses.GetByIdsAsync(ids, ct))?.FirstOrDefault(a => a.IsPrimary);
        if (address is null)
            return null;
        return await ToResponseAsync(address, ct);
    }

    public async Task<AddressResponse?> GetPrimaryAddressAsync(List<long> addressIds, CancellationToken ct = default)
    {
        var address = (await addresses.GetByIdsAsync(addressIds, ct))?.FirstOrDefault(a => a.IsPrimary);
        if (address is null)
            return null;

        return await ToResponseAsync(address, ct);
    }

    private async Task<int> GetProvinceIdAsync(int cityId, CancellationToken ct)
    {
        var province = await provinces.GetProvinceAsync(x => x.Cities.Any(c => c.Id == cityId), includeCities: false, ct: ct);
        return province?.Id ?? 0;
    }

    public async Task<Result> RemovePrimaryAsync(long addressId, CancellationToken ct = default)
    {
        var address = await addresses.GetByIdAsync(addressId, ct);
        if (address is null)
            return Result.Failure(Error.NotFound);

        address.RemovePrimary(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<bool> IsValidAddressLocationAsync(
        int cityId,
        int? districtId,
        CancellationToken cancellationToken)
    {
        var province = await provinces.GetProvinceAsync(x => x.Cities.Any(x => x.Id == cityId), ct: cancellationToken);
        if (province is null)
            return false;
        var city = province.GetCity(cityId);
        return city is not null && (districtId is null || city.GetDistrict(districtId.Value) != null);
    }

    public async Task<IReadOnlyList<AddressResponse>> GetAddressesAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];

        var found = await addresses.GetByIdsAsync(ids.ToList(), ct) ?? [];
        var ordered = found
            .Where(a => !IsDeleted(a))
            .OrderByDescending(a => a.IsPrimary)
            .ThenByDescending(a => a.CreatedAt);

        var responses = new List<AddressResponse>();
        foreach (var address in ordered)
            responses.Add(await ToResponseAsync(address, ct));
        return responses;
    }

    public async Task<Result<AddressResponse>> UpdateAddressAsync(long addressId,
        int cityId,
        int? districtId,
        string? addressText,
        string? postalCode,
        string? recipientName,
        string? recipientMobile,
        CancellationToken ct = default)
    {
        var address = await addresses.GetByIdAsync(addressId, ct);
        if (address is null || IsDeleted(address))
            return Result.Failure<AddressResponse>(AddressNotFound);
        if (address.IsPrimary)
            return Result.Failure<AddressResponse>(PrimaryAddressCannotBeModified);

        try
        {
            address.Update(cityId, districtId, addressText, postalCode, recipientName, recipientMobile, clock.UtcNow);
        }
        catch (DomainException dex)
        {
            return Result.Failure<AddressResponse>(new Error(dex.Code, dex.Message));
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(await ToResponseAsync(address, ct));
    }

    public async Task<Result<AddressResponse>> CorrectAddressLocationAsync(long addressId,
        int cityId,
        int? districtId,
        string? addressText,
        string? postalCode,
        CancellationToken ct = default)
    {
        var address = await addresses.GetByIdAsync(addressId, ct);
        if (address is null || IsDeleted(address))
            return Result.Failure<AddressResponse>(AddressNotFound);

        try
        {
            address.CorrectLocation(cityId, districtId, addressText, postalCode, clock.UtcNow);
        }
        catch (DomainException dex)
        {
            return Result.Failure<AddressResponse>(new Error(dex.Code, dex.Message));
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(await ToResponseAsync(address, ct));
    }

    public async Task<Result> DeleteAddressAsync(long addressId, CancellationToken ct = default)
    {
        var address = await addresses.GetByIdAsync(addressId, ct);
        if (address is null || IsDeleted(address))
            return Result.Failure(AddressNotFound);
        if (address.IsPrimary)
            return Result.Failure(PrimaryAddressCannotBeModified);

        address.Deleted(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<AddressResponse>> SetPrimaryAddressAsync(long addressId,
        IReadOnlyCollection<long> ownerAddressIds,
        CancellationToken ct = default)
    {
        var owned = await addresses.GetTrackedByIdsAsync(ownerAddressIds.Append(addressId).Distinct().ToList(), ct);
        var target = owned.FirstOrDefault(a => a.Id == addressId);
        if (target is null || IsDeleted(target))
            return Result.Failure<AddressResponse>(AddressNotFound);

        // Already primary: nothing to change, and no second primary may appear.
        if (target.IsPrimary)
            return Result.Success(await ToResponseAsync(target, ct));

        var now = clock.UtcNow;
        foreach (var previous in owned.Where(a => a.IsPrimary))
            previous.RemovePrimary(now);
        target.SetAsPrimary(now);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(await ToResponseAsync(target, ct));
    }

    private static bool IsDeleted(Address address) => address.StatusId == (int)AddressStatusType.Deleted;

    private async Task<AddressResponse> ToResponseAsync(Address address, CancellationToken ct) => new(
        address.Id,
        await GetProvinceIdAsync(address.CityId, ct),
        address.CityId,
        address.DistrictId,
        address.AddressText, address.PostalCode,
        address.RecipientName, address.RecipientMobile, address.IsPrimary,
        address.StatusId, address.CreatedAt, address.UpdatedAt);
}
