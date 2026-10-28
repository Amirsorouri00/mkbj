using EwaysVNext.Modules.Location.Contracts.Locations;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Location.Contracts.Abstractions;

public interface ILocationService
{
    Task<Result<CitiesResponse>> GetCityById(int cityId, CancellationToken ct = default);
    Task<Result<AddressResponse>> CreateAddressAsync(int cityId,
        int? districtId = null,
        string? addressText = null,
        string? postalCode = null,
        string? recipientName = null,
        string? recipientMobile = null,
        bool isPrimary = false,
        CancellationToken ct = default);
    Task<bool> IsValidAddressLocationAsync(
        int cityId,
        int? districtId,
        CancellationToken cancellationToken);
    Task<AddressResponse?> ExistsPrimaryAddressAsync(List<long> ids, CancellationToken ct);

    Task<IReadOnlyList<AddressResponse>> GetAddressesAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    Task<Result<AddressResponse>> UpdateAddressAsync(long addressId,
        int cityId,
        int? districtId,
        string? addressText,
        string? postalCode,
        string? recipientName,
        string? recipientMobile,
        CancellationToken ct = default);

    Task<Result<AddressResponse>> CorrectAddressLocationAsync(long addressId,
        int cityId,
        int? districtId,
        string? addressText,
        string? postalCode,
        CancellationToken ct = default);

    Task<Result> DeleteAddressAsync(long addressId, CancellationToken ct = default);

    Task<Result<AddressResponse>> SetPrimaryAddressAsync(long addressId,
        IReadOnlyCollection<long> ownerAddressIds,
        CancellationToken ct = default);
    Task<AddressResponse?> GetPrimaryAddressAsync(List<long> addressIds, CancellationToken ct = default);
    Task<Result> RemovePrimaryAsync(long addressId, CancellationToken ct = default);
}
