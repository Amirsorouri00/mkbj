using EwaysVNext.Modules.Payment.Application.Domain;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Application.Ports.Outbound;

/// <summary>
/// High-level gateway boundary owning provider selection, fallback, dispatch, call
/// deadlines and result validation. Callers pass inputs already validated by the service.
/// Callback and follow-up operations dispatch only the transaction's retained provider.
/// </summary>
public interface IPaymentGateway
{
    Task<Result<PaymentLinkResult>> CreateLinkAsync(IBankTransaction transaction, Uri callbackUrl, string? providerName, CancellationToken ct);
    Task<Result<PaymentCallbackResult>> VerifyCallbackAsync(IBankTransaction transaction, IReadOnlyDictionary<string, string> fields, CancellationToken ct);
    Task<Result<GatewayReversalResult>> ReverseAsync(IBankTransaction transaction, CancellationToken ct);
    Task<Result<GatewayInquiryResult>> InquiryAsync(IBankTransaction transaction, CancellationToken ct);
}
