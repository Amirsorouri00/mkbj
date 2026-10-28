using EwaysVNext.Modules.Payment.Application.Domain;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Application.Ports.Inbound;

/// <summary>
/// Payment's bank-independent gateway boundary. Every operation is scoped to the
/// existing BankTransaction; follow-up operations use only its retained gateway attempt.
/// Consumer modules do not interpret tokens or bank protocol fields.
/// </summary>
public interface IPaymentGatewayService
{
    /// <summary>A null <paramref name="providerName"/> uses configured selection and fallback; a named provider never falls back.</summary>
    Task<Result<PaymentLinkResult>> CreateLinkAsync(IBankTransaction transaction, Uri callbackUrl, string? providerName = null, CancellationToken ct = default);
    /// <summary>Checks the callback and, when it reports payment, confirms it with the bank; the returned attempt must be retained.</summary>
    Task<Result<PaymentCallbackResult>> VerifyCallbackAsync(IBankTransaction transaction, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default);
    Task<Result<GatewayReversalResult>> ReverseAsync(IBankTransaction transaction, CancellationToken ct = default);
    Task<Result<PaymentInquiryResult>> InquiryAsync(IBankTransaction transaction, CancellationToken ct = default);
}
