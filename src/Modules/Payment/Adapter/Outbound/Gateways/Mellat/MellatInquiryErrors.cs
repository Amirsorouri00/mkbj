using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat;

internal static class MellatInquiryErrors
{
    public static readonly Error NotConfigured = new(
        "MELLAT_INQUIRY_NOT_CONFIGURED", "Mellat transaction inquiry is not configured.", 503);

    public static Error Refusal(string code, string? message) => new(
        "MELLAT_INQUIRY_STATUS_" + code,
        string.IsNullOrWhiteSpace(message) ? "Mellat refused the inquiry request." : message,
        422);
}
