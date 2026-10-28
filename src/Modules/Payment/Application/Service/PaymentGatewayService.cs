using EwaysVNext.Modules.Payment.Application.Domain;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Payment.Application.Service.Errors;
using EwaysVNext.Modules.Payment.Application.Ports.Inbound;
using EwaysVNext.Modules.Payment.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Payment.Application.Service;

/// <summary>
/// Validates business inputs and interprets business outcomes. Provider selection,
/// dispatch, deadlines and result correlation belong to <see cref="IPaymentGateway"/>.
/// </summary>
public sealed class PaymentGatewayService(IPaymentGateway gateway, ILogger<PaymentGatewayService> logger) : IPaymentGatewayService
{
    public async Task<Result<PaymentLinkResult>> CreateLinkAsync(IBankTransaction transaction, Uri callbackUrl,
        string? providerName = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var error = TransactionError(transaction) ?? AmountError(transaction.Amount)
            ?? CallbackUrlError(callbackUrl) ?? ProviderNameError(providerName);
        if (error is not null)
            return Result.Failure<PaymentLinkResult>(error);

        return await gateway.CreateLinkAsync(transaction, callbackUrl, providerName, ct);
    }

    public async Task<Result<PaymentCallbackResult>> VerifyCallbackAsync(IBankTransaction transaction,
        IReadOnlyDictionary<string, string> fields, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var error = TransactionError(transaction) ?? AttemptError(transaction);
        if (error is not null)
            return Result.Failure<PaymentCallbackResult>(error);
        if (fields is null)
            return Result.Failure<PaymentCallbackResult>(PaymentServiceErrors.CallbackFieldsRequired);

        // Snapshot the aggregate before crossing the asynchronous external boundary.
        var bankTransactionId = transaction.Id;
        var attempt = transaction.GatewayAttempt!;

        // Our own duplicate policy: never confirm twice, whatever the bank would answer.
        if (attempt.IsConfirmed)
        {
            logger.LogInformation("Payment callback from {Provider} for bank transaction {BankTransactionId} is a duplicate of a confirmed attempt; the bank was not contacted",
                attempt.ProviderName, bankTransactionId);
            return Result.Success(new PaymentCallbackResult(attempt, GatewayPaymentState.Confirmed, null, IsDuplicate: true));
        }

        var result = await gateway.VerifyCallbackAsync(transaction, fields, ct);
        if (result.IsFailure)
            return result;

        var value = result.Value!;
        // Unknown still needs follow-up; it is never a final failure.
        logger.Log(value.State == GatewayPaymentState.Unknown ? LogLevel.Warning : LogLevel.Information,
            "Payment callback from {Provider} for bank transaction {BankTransactionId} processed: {State} (provider code {ProviderCode})",
            attempt.ProviderName, bankTransactionId, value.State, value.ProviderCode);
        return result;
    }

    public async Task<Result<GatewayReversalResult>> ReverseAsync(IBankTransaction transaction, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var error = TransactionError(transaction) ?? AttemptError(transaction);
        if (error is not null)
            return Result.Failure<GatewayReversalResult>(error);

        return await gateway.ReverseAsync(transaction, ct);
    }

    public async Task<Result<PaymentInquiryResult>> InquiryAsync(IBankTransaction transaction, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var error = TransactionError(transaction) ?? AttemptError(transaction);
        if (error is not null)
            return Result.Failure<PaymentInquiryResult>(error);

        var result = await gateway.InquiryAsync(transaction, ct);
        if (result.IsFailure)
            return Result.Failure<PaymentInquiryResult>(result.Error!);

        var bankRecord = result.Value!;
        return Result.Success(new PaymentInquiryResult(bankRecord, Equals(bankRecord.BankAmount, transaction.Amount)));
    }

    private static Error? TransactionError(IBankTransaction? transaction) =>
        transaction is null ? PaymentServiceErrors.TransactionRequired
        : transaction.Id <= 0 ? PaymentServiceErrors.TransactionIdInvalid
        : null;

    private static Error? AttemptError(IBankTransaction transaction) =>
        transaction.GatewayAttempt is null ? PaymentServiceErrors.GatewayAttemptRequired : null;

    private static Error? AmountError(Money? amount) =>
        amount is null ? PaymentServiceErrors.AmountRequired
        : amount.Amount <= 0 ? PaymentServiceErrors.AmountInvalid
        : null;

    private static Error? CallbackUrlError(Uri? uri) =>
        uri is { IsAbsoluteUri: true } && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? null
            : PaymentServiceErrors.CallbackUrlInvalid;

    private static Error? ProviderNameError(string? providerName) =>
        providerName is not null && string.IsNullOrWhiteSpace(providerName) ? PaymentServiceErrors.ProviderNameEmpty : null;
}
