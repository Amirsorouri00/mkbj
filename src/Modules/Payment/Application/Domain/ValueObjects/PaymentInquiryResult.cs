namespace EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

public sealed record PaymentInquiryResult(
    GatewayInquiryResult BankRecord,
    bool AmountMatchesExpected);
