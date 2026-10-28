using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Products.Dtos;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Products.Endpoints;

public sealed class ProductEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalog").WithTags("Catalog");

        group.MapPost("/products", async (IProductService svc, CreateProductRequest req, CancellationToken ct) =>
        {
            var result = await svc.CreateProductAsync(req.Sku, req.Name, req.Price, req.Currency, req.InitialStock, req.Description, ct);
            return result.ToCreatedHttpResult(v => $"/api/v1/catalog/products/{v.Id}");
        }).RequireAuthorization("Admin");

        group.MapPost("/products/{id:long}/price", async (IProductService svc, long id, ChangePriceRequest req, CancellationToken ct) =>
        {
            if (id != req.ProductId) return Result.Failure("PRODUCT_ID_MISMATCH", "Mismatched product id.").ToHttpResult();
            var result = await svc.ChangePriceAsync(id, req.NewPrice, req.Currency, ct);
            return result.ToHttpResult();
        }).RequireAuthorization("Admin");

        group.MapPost("/products/{id:long}/stock", async (IProductService svc, long id, AdjustStockRequest req, CancellationToken ct) =>
        {
            if (id != req.ProductId) return Result.Failure("PRODUCT_ID_MISMATCH", "Mismatched product id.").ToHttpResult();
            var result = await svc.AdjustStockAsync(id, req.Delta, ct);
            return result.ToHttpResult();
        }).RequireAuthorization("Admin");

        group.MapPost("/products/{id:long}/reserve", async (IProductService svc, long id, ReserveStockRequest req, CancellationToken ct) =>
        {
            var ttl = req.TtlMinutes > 0 ? TimeSpan.FromMinutes(req.TtlMinutes) : TimeSpan.FromMinutes(10);
            var result = await svc.ReserveStockAsync(id, req.Quantity, ttl, ct);
            return result.ToHttpResult();
        }).RequireAuthorization("Admin");

        group.MapPost("/products/{id:long}/reserve/{reservationId:long}/commit", async (IProductService svc, long id, long reservationId, CancellationToken ct) =>
        {
            var result = await svc.CommitReservationAsync(id, reservationId, ct);
            return result.ToHttpResult();
        }).RequireAuthorization("Admin");

        group.MapPost("/products/{id:long}/reserve/{reservationId:long}/release", async (IProductService svc, long id, long reservationId, CancellationToken ct) =>
        {
            var result = await svc.ReleaseReservationAsync(id, reservationId, ct);
            return result.ToHttpResult();
        }).RequireAuthorization("Admin");
    }
}
