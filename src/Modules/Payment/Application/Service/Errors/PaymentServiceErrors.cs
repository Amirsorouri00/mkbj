using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Application.Service.Errors;

/// <summary>Business-input refusals returned by PaymentGatewayService before any gateway work.</summary>
public static class PaymentServiceErrors
{
    public static readonly Error TransactionRequired = new("PAYMENT_TRANSACTION_REQUIRED", "A bank transaction is required.");
    public static readonly Error TransactionIdInvalid = new("PAYMENT_TRANSACTION_ID_INVALID", "The bank transaction ID must be positive.");
    public static readonly Error AmountRequired = new("PAYMENT_AMOUNT_REQUIRED", "The bank transaction amount is required.");
    public static readonly Error AmountInvalid = new("PAYMENT_AMOUNT_INVALID", "The bank transaction amount must be positive.");
    public static readonly Error CallbackUrlInvalid = new("PAYMENT_CALLBACK_URL_INVALID", "The callback address must be an absolute HTTP or HTTPS URL.");
    public static readonly Error ProviderNameEmpty = new("PAYMENT_PROVIDER_NAME_EMPTY", "An explicitly requested provider name cannot be empty.");
    public static readonly Error CallbackFieldsRequired = new("PAYMENT_CALLBACK_FIELDS_REQUIRED", "Callback fields are required.");
    public static readonly Error GatewayAttemptRequired = new("PAYMENT_GATEWAY_ATTEMPT_REQUIRED", "The bank transaction has no retained gateway attempt.");
}
