using EwaysVNext.Modules.Location.Application.Ports.Inbound;
using EwaysVNext.Modules.MetaData.Adapter.Inbound.Rest.Dtos;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Location.Adapter.Inbound.Rest.Endpoints;

public sealed class LocationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/Locations").WithTags("Locations");

        group.MapGet("/provinces", GetProvinces)
            .WithName("GetProvinces")
            .WithDescription("Get list of all provinces")
            .AllowAnonymous();

        group.MapGet("/provinces/{provinceId:int}/cities", GetCitiesByProvinceId)
            .WithName("GetCitiesByProvinceId")
            .WithDescription("Get cities of a specific province")
            .AllowAnonymous();

        group.MapGet("/cities/{cityId:int}/districts", GetDistrictByCityId)
            .WithName("GetDistrictByCityId")
            .WithDescription("Get districts of a specific city")
            .AllowAnonymous();
    }

    private static async Task<IResult> GetProvinces(IProvinceInboundPort svc, CancellationToken ct)
    {
        var r = await svc.GetProvinceAsync(ct: ct);
        return r.ToHttpResult(list => list.Select(x => new LocationResponse(x.Id, x.Name)).ToList());
    }

    private static async Task<IResult> GetCitiesByProvinceId(
        int provinceId,
        IProvinceInboundPort svc,
        CancellationToken ct)
    {
        var r = await svc.GetCitiesByProvinceId(provinceId: provinceId, ct: ct);
        return r.ToHttpResult(list => list.Select(x => new LocationResponse(x.Id, x.Name, x.Districts.Count > 0)).ToList());
    }

    private static async Task<IResult> GetDistrictByCityId(
        int cityId,
        IProvinceInboundPort svc,
        CancellationToken ct)
    {
        var r = await svc.GetDistrictByCityId(cityId: cityId, ct: ct);
        return r.ToHttpResult(list => list.Select(x => new LocationResponse(x.Id, x.Name, false)).ToList());
    }
}
