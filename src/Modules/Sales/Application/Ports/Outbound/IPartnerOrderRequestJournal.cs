using Mkbj.Modules.Sales.Application.Domain.Telemetry;

namespace Mkbj.Modules.Sales.Application.Ports.Outbound;

public interface IPartnerOrderRequestJournal
{
    Task ReceiveAsync(PartnerOrderRequest request, CancellationToken ct);
    Task CompleteAsync(long id, DateTimeOffset at, int statusCode, string responseBody,
        bool replayed, CancellationToken ct);
}
