namespace EwaysVNext.Modules.Notification.Adapter.Outbound.Sms.PayamSms;

public sealed class PayamSmsOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
}
