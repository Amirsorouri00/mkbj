namespace EwaysVNext.Modules.Notification.Adapter.Outbound.Sms.PayamSms;

internal static class PayamSmsProviderMessages
{
    public const string NoSendResult = "Send returned no result.";
    public const string InvalidRecipientFormat = "Recipient '{0}' is not a valid Iranian mobile number.";
    public const string HttpErrorFormat = "HTTP {0}: {1}";
    public const string SoapFault = "SOAP fault";
}
