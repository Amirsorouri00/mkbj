namespace EwaysVNext.Modules.Location.Contracts.Locations;

public sealed record DistrictResponse(int id, string name);
public sealed record CitiesResponse(int id, string name, List<DistrictResponse>? districts = null, int provinceId = 0, string? code = null)
{
    public DistrictResponse? GetDistrictById(int id) => districts?.FirstOrDefault(x => x.id == id);
}
public sealed record AddressResponse(long id, int provinceId, int cityId,
        int? districtId,
        string? addressText,
        string? postalCode,
        string? recipientName,
        string? recipientMobile,
        bool isPrimary,
        int statusId,
        DateTime createdAt,
        DateTime? updatedAt);
