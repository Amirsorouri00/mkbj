using EwaysVNext.Modules.Location.Application.Domain.Locations;
using EwaysVNext.Modules.Location.Application.Ports.Outbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Location.Application.Service;
public sealed class ProvinceService(IProvinceRepository provinces) : Ports.Inbound.IProvinceInboundPort
{
    public async Task<Result<IList<City>>> GetCitiesByProvinceId(int provinceId,
        CancellationToken ct = default)
    {
        var province = await provinces.GetProvinceAsync(x => x.Id == provinceId,true, ct);
        if (province is null)
            return Result.Failure<IList<City>>(Error.NotFound);
        return Result.Success(province.GetCities());
    }

    public async Task<Result<IList<District>>> GetDistrictByCityId(int cityId, CancellationToken ct = default)
    {
        var province = await provinces.GetProvinceAsync(x => x.Cities.Any(x => x.Id == cityId), ct: ct);
        if (province is null)
            return Result.Failure<IList<District>>(Error.NotFound);
        
        return Result.Success(province.GetDistrictsByCity(cityId));
    }
    public async Task<Result<City?>> GetCityById(int cityId, CancellationToken ct = default)
    {
        var province = await provinces.GetProvinceAsync(x => x.Cities.Any(x => x.Id == cityId), ct: ct);
        if (province is null)
            return Result.Failure<City?>(Error.NotFound);

        return Result.Success(province.GetCity(cityId));
    }
    public async Task<Result<IList<Province>>> GetProvinceAsync(CancellationToken ct = default)
    {
        return Result.Success(await provinces.GetAllProvincesAsync(ct: ct));
    }
}
