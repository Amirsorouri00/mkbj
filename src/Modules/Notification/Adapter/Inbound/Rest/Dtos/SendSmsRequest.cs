namespace EwaysVNext.Modules.Notification.Adapter.Inbound.Rest.Dtos;

public sealed record SendSmsRequest(string Recipient, string Body);
