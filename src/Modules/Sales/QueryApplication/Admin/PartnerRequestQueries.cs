using System.Text.Json.Serialization;
using Mkbj.Database;
using Mkbj.Modules.Sales.Application.Domain.Telemetry;
using Mkbj.Modules.Sales.QueryApplication.Partner;
using Microsoft.EntityFrameworkCore;

namespace Mkbj.Modules.Sales.QueryApplication.Admin;

public sealed record PartnerRequestSummary([property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id, [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long PartnerId, DateTimeOffset ReceivedAt,
    DateTimeOffset? CompletedAt, string CorrelationId, string Path, string[] ExternalOrderIds,
    int? StatusCode, bool Replayed);
public sealed record PartnerRequestDocument([property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id, string Number);
public sealed record PartnerRequestOrder([property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id, string Number, string? ExternalOrderId,
    IReadOnlyList<PartnerRequestDocument> Documents);
public sealed record PartnerRequestDetail(PartnerRequestSummary Request, string RequestBody,
    string? ResponseBody, IReadOnlyList<PartnerRequestOrder> Orders);

public interface IPartnerRequestQueries
{
    Task<CursorPage<PartnerRequestSummary>> ListAsync(string? search, long? cursor, CancellationToken ct);
    Task<PartnerRequestDetail?> GetAsync(long id, CancellationToken ct);
}

internal sealed class PartnerRequestQueries(MkbjDbContext db) : IPartnerRequestQueries
{
    public async Task<CursorPage<PartnerRequestSummary>> ListAsync(string? search, long? cursor, CancellationToken ct)
    {
        var query = db.Set<PartnerOrderRequest>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.CorrelationId == search || x.ExternalOrderIds.Contains(search));
        if (cursor.HasValue) query = query.Where(x => x.Id < cursor.Value);
        var rows = await query.OrderByDescending(x => x.Id).Take(51).Select(x => new PartnerRequestSummary(
            x.Id, x.PartnerId, x.ReceivedAt, x.CompletedAt, x.CorrelationId, x.Path,
            x.ExternalOrderIds, x.StatusCode, x.Replayed)).ToListAsync(ct);
        return new(rows.Take(50).ToArray(), rows.Count > 50 ? rows[49].Id : null);
    }

    public async Task<PartnerRequestDetail?> GetAsync(long id, CancellationToken ct)
    {
        var row = await db.Set<PartnerOrderRequest>().AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { Summary = new PartnerRequestSummary(x.Id, x.PartnerId, x.ReceivedAt,
                x.CompletedAt, x.CorrelationId, x.Path, x.ExternalOrderIds, x.StatusCode, x.Replayed),
                x.RequestBody, x.ResponseBody }).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var partnerId = row.Summary.PartnerId;
        var externalIds = row.Summary.ExternalOrderIds;
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.PartnerId == partnerId && externalIds.Contains(o.ExternalOrderId!))
            .Select(o => new PartnerRequestOrder(o.Id, o.Number, o.ExternalOrderId,
                o.Documents.Select(d => new PartnerRequestDocument(d.Id, d.Number)).ToList()))
            .ToListAsync(ct);
        return new(row.Summary, row.RequestBody, row.ResponseBody, orders);
    }
}
