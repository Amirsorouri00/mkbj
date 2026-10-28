using System.Linq.Expressions;
using EwaysVNext.Modules.Location.Application.Domain.Locations;
using EwaysVNext.Modules.Location.Application.Ports.Inbound;
using EwaysVNext.Modules.Location.Application.Ports.Outbound;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;

namespace EwaysVNext.Modules.Location.Adapter.Inbound.GraphQL.Locations;

[ExtendObjectType(typeof(Query))]
public sealed class LocationQueries
{
    public async Task<IEnumerable<Province>> Provinces(
        [Service] IProvinceRepository repository,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        Expression<Func<Province, bool>>? predicate = name is null
            ? null
            : p => p.Name.Contains(name);

        return await repository.GetAllProvincesAsync(predicate, cancellationToken);
    }

    public async Task<IEnumerable<City>> CitiesByProvinceId(
        [Service] IProvinceInboundPort port,
        int provinceId,
        CancellationToken cancellationToken) =>
        (await port.GetCitiesByProvinceId(provinceId, cancellationToken)).ValueOrThrow();

    public async Task<IEnumerable<District>> DistrictsByCityId(
        [Service] IProvinceInboundPort port,
        int cityId,
        CancellationToken cancellationToken) =>
        (await port.GetDistrictByCityId(cityId, cancellationToken)).ValueOrThrow();

    public async Task<City?> CityById(
        [Service] IProvinceInboundPort port,
        int cityId,
        CancellationToken cancellationToken) =>
        (await port.GetCityById(cityId, cancellationToken)).ValueOrDefault();
}
