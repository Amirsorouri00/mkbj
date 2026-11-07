using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Common;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.QueryApplication.Partner;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Partner;

public sealed record SearchInvoicesRequest(IReadOnlyList<string>? ExternalOrderIds);

public sealed record InvoiceSearchResponse(IReadOnlyList<InvoiceSearchItemResponse> Items);

/// <remarks>
/// <c>Status</c> rather than a bare presence check, so a partner reconciling a thousand orders
/// can tell "we have no invoice for this" from "we never heard of this".
/// </remarks>
public sealed record InvoiceSearchItemResponse(
    string ExternalOrderId, string Status, IReadOnlyList<PartnerInvoiceDto> Invoices);

/// <summary>
/// A partner's own invoices: the document, the PDF, and a bulk lookup.
/// </summary>
public sealed class PartnerInvoiceEndpoints : IEndpoint
{
    private const string BasePath = "/api/v1/invoices";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var invoices = app.MapGroup(BasePath)
            .WithTags("Partner Invoices")
            .RequireAuthorization(AuthPolicies.Partner(PartnerScopes.InvoicesRead));

        invoices.MapGet("/{id:long}", GetAsync);
        invoices.MapGet("/{id:long}/pdf", GetPdfAsync);
        invoices.MapPost("/search", SearchAsync);
    }

    private static async Task<IResult> GetAsync(
        long id, HttpContext context, IPartnerOrderQueries queries, CancellationToken ct)
    {
        if (!PartnerPrincipal.TryFrom(context.User, out var principal))
            return EndpointResult.Unauthorized();

        var invoice = await queries.GetInvoiceAsync(principal!, id, ct);

        return invoice.IsFailure
            ? EndpointResult.Fail(invoice.Error!)
            : PartnerResults.Ok(invoice.Value!);
    }

    /// <summary>
    /// Redirects to a short-lived link for the PDF, rendering it on first request.
    /// </summary>
    /// <remarks>
    /// A redirect rather than proxying the bytes: the object store serves the file directly, so
    /// a large download never occupies an application thread. The caller follows one hop, which
    /// every HTTP client does by default.
    /// </remarks>
    private static async Task<IResult> GetPdfAsync(
        long id, HttpContext context, IInvoiceService invoices, CancellationToken ct)
    {
        if (!PartnerPrincipal.TryFrom(context.User, out var principal))
            return EndpointResult.Unauthorized();

        var url = await invoices.GetPdfUrlAsync(
            new DocumentAccess.Partner(principal!.PartnerId), id, ct);

        return url.IsFailure
            ? EndpointResult.Fail(url.Error!)
            : EndpointResult.RedirectTo(url.Value!);
    }

    private static async Task<IResult> SearchAsync(
        HttpContext context, IPartnerOrderQueries queries, CancellationToken ct)
    {
        if (!PartnerPrincipal.TryFrom(context.User, out var principal))
            return EndpointResult.Unauthorized();

        var read = await PartnerResults.ReadAsync<SearchInvoicesRequest>(context, ct);
        if (read.IsFailure) return EndpointResult.Fail(read.Error!);

        var ids = read.Value!.ExternalOrderIds ?? [];
        var found = await queries.SearchInvoicesAsync(principal!, ids, ct);

        if (found.IsFailure) return EndpointResult.Fail(found.Error!);

        return PartnerResults.Ok(new InvoiceSearchResponse(
            [.. found.Value!.Select(item => new InvoiceSearchItemResponse(
                item.ExternalOrderId,
                item.Found ? "found" : "not_found",
                item.Invoices))]));
    }
}
