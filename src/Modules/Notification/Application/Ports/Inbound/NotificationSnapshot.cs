namespace EwaysVNext.Modules.Notification.Application.Ports.Inbound;

public sealed record NotificationSnapshot(string Recipient, string Body, string ProviderName, string ProviderMessageId);
