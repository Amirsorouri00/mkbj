using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Notification.Application.Ports.Outbound;

public interface ISmsProvider
{
    string Name { get; }

    Task<Result<SmsSendResult>> SendAsync(string recipient, string body, CancellationToken ct = default);
}
