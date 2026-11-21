using Mkbj.Database;
using Mkbj.Modules.Sales.Application.Domain.Telemetry;
using Mkbj.Modules.Sales.Application.Ports.Outbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Sales.Adapter.Outbound.Persistence;

internal sealed class PartnerOrderRequestJournal(IServiceScopeFactory scopes) : IPartnerOrderRequestJournal
{
    // Separate scopes prevent a journal write from flushing a partially built order graph.
    // These are durable receipts; failures propagate, unlike best-effort usage telemetry.
    public async Task ReceiveAsync(PartnerOrderRequest request, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MkbjDbContext>();
        db.Set<PartnerOrderRequest>().Add(request);
        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(long id, DateTimeOffset at, int statusCode,
        string responseBody, bool replayed, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MkbjDbContext>();
        var receipt = await db.Set<PartnerOrderRequest>().SingleAsync(x => x.Id == id, ct);
        receipt.CompletedAt = at;
        receipt.StatusCode = statusCode;
        receipt.ResponseBody = responseBody;
        receipt.Replayed = replayed;
        await db.SaveChangesAsync(ct);
    }
}
