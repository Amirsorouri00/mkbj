using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;

public static class PaymentGatewayErrors
{
    public static readonly Error NotConfigured = new("PAYMENT_GATEWAY_NOT_CONFIGURED", "No payment gateway is configured.", 503);
    public static readonly Error Exhausted = new("PAYMENT_GATEWAYS_EXHAUSTED", "No configured gateway could create a payment link.", 503);
    public static readonly Error InvalidRequest = new("PAYMENT_REQUEST_INVALID", "Payment identity, amount, or callback address is missing or malformed.");
    public static readonly Error UnsupportedCurrency = new("PAYMENT_CURRENCY_UNSUPPORTED", "The selected gateway does not support this currency.");
    public static readonly Error UnsupportedAmount = new("PAYMENT_AMOUNT_UNSUPPORTED", "The selected gateway cannot accept this amount.");
    public static readonly Error UnsupportedCallback = new("PAYMENT_CALLBACK_UNSUPPORTED", "The selected gateway cannot accept this callback address.");
    public static readonly Error InvalidContext = new("PAYMENT_CONTEXT_INVALID", "The original gateway payment context is invalid.");
    public static readonly Error InvalidCallback = new("PAYMENT_CALLBACK_INVALID", "Callback references do not match the original payment.");
    public static readonly Error InvalidResponse = new("PAYMENT_GATEWAY_RESPONSE_INVALID", "The gateway returned an invalid response.", 502);
    public static readonly Error Timeout = new("PAYMENT_GATEWAY_TIMEOUT", "The gateway response deadline elapsed; its outcome may be unknown.", 504);
    public static readonly Error Unavailable = new("PAYMENT_GATEWAY_UNAVAILABLE", "The gateway could not be reached; its outcome may be unknown.", 503);
    public static readonly Error UnsupportedOperation = new("PAYMENT_OPERATION_UNSUPPORTED", "The gateway operation is not supported.");
    public static readonly Error TransactionNotFound = new("PAYMENT_GATEWAY_TRANSACTION_NOT_FOUND", "No correlated bank transaction was found.", 404);
}
