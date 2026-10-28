using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Location.Application.Domain.Locations;

namespace EwaysVNext.Modules.Location.Application.Ports.Inbound;

public interface IProvinceInboundPort
{
    Task<Result<IList<Province>>> GetProvinceAsync(CancellationToken ct=default);
    Task<Result<IList<City>>> GetCitiesByProvinceId(int provinceId, CancellationToken ct=default);
    Task<Result<IList<District>>> GetDistrictByCityId(int cityId, CancellationToken ct=default);
    Task<Result<City?>> GetCityById(int cityId, CancellationToken ct = default);
}
