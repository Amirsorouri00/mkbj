using EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Dtos;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Endpoints;

public sealed class CatalogEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalog").WithTags("Catalog");

        group.MapPost("/products", async (IProductService svc, CreateProductRequest req, CancellationToken ct) =>
        {
            var result = await svc.CreateProductAsync(req.Sku, req.Name, req.Price, req.Currency, req.Description, ct);
            return result.ToCreatedHttpResult(v => $"/api/v1/catalog/products/{v.Id}");
        }).RequireAuthorization("Admin");

        group.MapPost("/products/{id:long}/price", async (IProductService svc, long id, ChangePriceRequest req, CancellationToken ct) =>
        {
            if (id != req.ProductId) return Result.Failure("PRODUCT_ID_MISMATCH", "Mismatched product id.").ToHttpResult();
            var result = await svc.ChangePriceAsync(id, req.NewPrice, req.Currency, ct);
            return result.ToHttpResult();
        }).RequireAuthorization("Admin");
    }
}
