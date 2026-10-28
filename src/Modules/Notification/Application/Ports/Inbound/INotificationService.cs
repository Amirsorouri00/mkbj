using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Notification.Application.Ports.Inbound;

public interface INotificationService
{
    Task<Result<NotificationSnapshot>> SendSmsAsync(string recipient, string body, CancellationToken ct = default);
}
