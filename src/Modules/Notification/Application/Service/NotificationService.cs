using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Notification.Application.Ports.Inbound;
using EwaysVNext.Modules.Notification.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Notification.Application.Service;

public sealed class NotificationService(ISmsProviderResolver smsProviders) : INotificationService
{
    public async Task<Result<NotificationSnapshot>> SendSmsAsync(string recipient, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            return Result.Failure<NotificationSnapshot>(new Error("NOTIFICATION_RECIPIENT_EMPTY", NotificationServiceMessages.RecipientEmpty));
        if (string.IsNullOrWhiteSpace(body))
            return Result.Failure<NotificationSnapshot>(new Error("NOTIFICATION_BODY_EMPTY", NotificationServiceMessages.BodyEmpty));

        var providerResult = smsProviders.Resolve();
        if (providerResult.IsFailure)
            return Result.Failure<NotificationSnapshot>(providerResult.Error!);
        var provider = providerResult.Value!;

        var trimmedRecipient = recipient.Trim();
        var trimmedBody = body.Trim();

        var sendResult = await provider.SendAsync(trimmedRecipient, trimmedBody, ct);
        if (sendResult.IsFailure)
            return Result.Failure<NotificationSnapshot>(sendResult.Error!);

        return Result.Success(new NotificationSnapshot(trimmedRecipient, trimmedBody, provider.Name, sendResult.Value!.ProviderMessageId));
    }
}
