using System.Collections.ObjectModel;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Payment.Application.Domain;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Payment.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.Logging;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;

internal sealed class PaymentGateway(
    IPaymentGatewayProviderSelector providers, IGatewayCallExecutor executor,
    IIdGenerator ids, TimeProvider timeProvider, ILogger<PaymentGateway> logger) : IPaymentGateway
{
    public async Task<Result<PaymentLinkResult>> CreateLinkAsync(IBankTransaction transaction, Uri callbackUrl,
        string? providerName, CancellationToken ct)
    {
        // Snapshot the aggregate before crossing the asynchronous external boundary.
        long transactionId = transaction.Id;
        Money amount = transaction.Amount;
        bool explicitlySelected = providerName is not null;

        var plan = providers.SelectForLinkCreation(providerName);
        if (plan.IsFailure)
            return Result.Failure<PaymentLinkResult>(plan.Error!);

        foreach (var provider in plan.Value!.Providers)
        {
            ct.ThrowIfCancellationRequested();
            var result = await TryCreateLinkAsync(provider, transactionId, amount, callbackUrl, plan.Value.Timeout, ct);
            if (result.IsSuccess || explicitlySelected)
                return result;
            logger.LogWarning("Payment link creation with {Provider} failed ({ErrorCode}); trying the next configured provider",
                provider.Name, result.Error!.Code);
        }

        return Result.Failure<PaymentLinkResult>(PaymentGatewayErrors.Exhausted);
    }

    public async Task<Result<PaymentCallbackResult>> VerifyCallbackAsync(IBankTransaction transaction,
        IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        var payment = Context(transaction);
        var resolved = providers.SelectRetained(payment.ProviderName);
        if (resolved.IsFailure)
            return CallbackFailure(payment, resolved.Error!);

        var provider = resolved.Value!;
        var request = new GatewayCallbackRequest(payment, Snapshot(fields));
        var result = await executor.ExecuteAsync(provider.Name,
            token => provider.VerifyCallbackAsync(request, token), timeout: null, ct);
        if (result.IsFailure)
            return CallbackFailure(payment, result.Error!);

        var value = result.Value;
        if (value is null || !Enum.IsDefined(value.State)
            || (value.State == GatewayPaymentState.Confirmed && string.IsNullOrWhiteSpace(value.ProviderReference)))
            return CallbackFailure(payment, PaymentGatewayErrors.InvalidResponse);
        if (value.RequestId != payment.RequestId
            || !string.Equals(value.Token, payment.Token, StringComparison.Ordinal)
            || (payment.ProviderReference is not null && value.ProviderReference is not null
                && !string.Equals(value.ProviderReference, payment.ProviderReference, StringComparison.Ordinal)))
        {
            return CallbackFailure(payment, PaymentGatewayErrors.InvalidCallback);
        }

        return Result.Success(new PaymentCallbackResult(Retained(payment.Attempt, value), value.State, value.ProviderCode, IsDuplicate: false));
    }

    public async Task<Result<GatewayReversalResult>> ReverseAsync(IBankTransaction transaction, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var resolved = ResolveRetained<IPaymentReversalProvider>(transaction);
        Result<GatewayReversalResult> result;
        if (resolved.IsFailure)
            result = Result.Failure<GatewayReversalResult>(resolved.Error!);
        else
        {
            var target = resolved.Value!;
            result = await executor.ExecuteAsync(target.ProviderName,
                token => target.Capability.ReverseAsync(target.Payment, token), timeout: null, ct);
            if (result.IsSuccess && (result.Value is null || !Enum.IsDefined(result.Value.State)))
                result = Result.Failure<GatewayReversalResult>(PaymentGatewayErrors.InvalidResponse);
        }

        LogOperation(transaction, "Reverse", result.IsSuccess ? result.Value!.State.ToString() : result.Error!.Code);
        return result;
    }

    public async Task<Result<GatewayInquiryResult>> InquiryAsync(IBankTransaction transaction, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var resolved = ResolveRetained<IPaymentInquiryProvider>(transaction);
        Result<GatewayInquiryResult> result;
        if (resolved.IsFailure)
            result = Result.Failure<GatewayInquiryResult>(resolved.Error!);
        else
        {
            var target = resolved.Value!;
            if (string.IsNullOrWhiteSpace(target.Payment.ProviderReference))
                result = Result.Failure<GatewayInquiryResult>(PaymentGatewayErrors.InvalidContext);
            else
            {
                var observation = await executor.ExecuteAsync(target.ProviderName,
                    token => target.Capability.InquiryAsync(target.Payment, token), timeout: null, ct);
                result = MapInquiry(target.Payment, observation);
            }
        }

        LogOperation(transaction, "Inquiry", result.IsSuccess ? "Succeeded" : result.Error!.Code);
        return result;
    }

    private async Task<Result<PaymentLinkResult>> TryCreateLinkAsync(IPaymentGatewayProvider provider, long transactionId,
        Money amount, Uri callbackUrl, TimeSpan timeout, CancellationToken ct)
    {
        var request = new GatewayLinkRequest(ids.Generate(), transactionId, amount, callbackUrl);
        var result = await executor.ExecuteAsync(provider.Name,
            token => provider.CreateLinkAsync(request, token), timeout, ct);
        if (result.IsFailure)
            return Result.Failure<PaymentLinkResult>(result.Error!);

        var link = result.Value;
        if (link is null || string.IsNullOrWhiteSpace(link.Token) || link.Redirect is null
            || !IsWebAddress(link.Redirect.Url) || !Enum.IsDefined(link.Redirect.Method) || link.Redirect.Fields is null)
            return Result.Failure<PaymentLinkResult>(PaymentGatewayErrors.InvalidResponse);

        var attempt = GatewayAttempt.Create(provider.Name, request.RequestId, link.Token, timeProvider.GetUtcNow());
        return Result.Success(new PaymentLinkResult(attempt, link.Redirect with { Fields = Snapshot(link.Redirect.Fields) }));
    }

    // Mechanical resolution to the stored provider: no policy, candidates, or fallback.
    private Result<ResolvedCapability<TCapability>> ResolveRetained<TCapability>(IBankTransaction transaction)
        where TCapability : class
    {
        var payment = Context(transaction);
        var resolved = providers.SelectRetained(payment.ProviderName);
        if (resolved.IsFailure)
            return Result.Failure<ResolvedCapability<TCapability>>(resolved.Error!);
        if (resolved.Value is not TCapability capability)
            return Result.Failure<ResolvedCapability<TCapability>>(PaymentGatewayErrors.UnsupportedOperation);

        return Result.Success(new ResolvedCapability<TCapability>(payment, resolved.Value.Name, capability));
    }

    private static Result<GatewayInquiryResult> MapInquiry(GatewayPaymentContext payment,
        Result<GatewayInquiryObservation> observation)
    {
        if (observation.IsFailure)
            return Result.Failure<GatewayInquiryResult>(observation.Error!);

        var value = observation.Value;
        if (value is null || value.BankAmount is null || value.BankAmount.Amount <= 0
            || !string.Equals(value.BankAmount.Currency, "IRR", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(value.ProviderReference)
            || !string.Equals(value.ProviderReference, payment.ProviderReference, StringComparison.Ordinal))
        {
            return Result.Failure<GatewayInquiryResult>(PaymentGatewayErrors.InvalidResponse);
        }

        return Result.Success(new GatewayInquiryResult(value.BankAmount, value.ProviderReference,
            value.TransactionTime, value.ProviderStatus, value.ProviderCode, value.ProviderMessage));
    }

    private void LogOperation(IBankTransaction transaction, string operation, string outcome)
    {
        logger.LogInformation("Payment gateway {Provider} operation {Operation} for bank transaction {BankTransactionId} completed with {Outcome}",
            transaction.GatewayAttempt?.ProviderName ?? "unknown", operation, transaction.Id, outcome);
    }

    private sealed record ResolvedCapability<TCapability>(
        GatewayPaymentContext Payment,
        string ProviderName,
        TCapability Capability);

    // A confirmed attempt carries the bank reference later operations (e.g. Mellat Reverse) need.
    private static GatewayAttempt Retained(GatewayAttempt attempt, GatewayCallbackResult value) =>
        value.State == GatewayPaymentState.Confirmed ? attempt.WithConfirmation(value.ProviderReference!)
        : value.ProviderReference is not null && attempt.ProviderReference is null ? attempt.WithProviderReference(value.ProviderReference)
        : attempt;

    private Result<PaymentCallbackResult> CallbackFailure(GatewayPaymentContext payment, Error error)
    {
        if (error == PaymentGatewayErrors.InvalidCallback || error == PaymentGatewayErrors.InvalidContext)
            logger.LogWarning(SuspiciousCallback,
                "Suspicious payment callback from {Provider} for bank transaction {BankTransactionId} rejected ({ErrorCode})",
                payment.ProviderName, payment.BankTransactionId, error.Code);
        else
            logger.LogWarning("Payment callback from {Provider} for bank transaction {BankTransactionId} rejected ({ErrorCode})",
                payment.ProviderName, payment.BankTransactionId, error.Code);
        return Result.Failure<PaymentCallbackResult>(error);
    }

    // Failed checks and malformed callbacks: filterable apart from operational failures.
    internal static readonly EventId SuspiciousCallback = new(4101, "PaymentCallbackSuspicious");

    // Built only from the BankTransaction and its retained attempt, never from callback fields.
    private static GatewayPaymentContext Context(IBankTransaction transaction) =>
        new(transaction.Id, transaction.Amount, transaction.GatewayAttempt!);

    private static bool IsWebAddress(Uri? uri) =>
        uri is { IsAbsoluteUri: true } && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static ReadOnlyDictionary<string, string> Snapshot(IReadOnlyDictionary<string, string> fields) =>
        new(new Dictionary<string, string>(fields, StringComparer.Ordinal));
}
