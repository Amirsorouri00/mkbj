using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

internal static class ParsianInquiryErrors
{
    public static readonly Error TokenProviderUnavailable = new(
        "PARSIAN_INQUIRY_TOKEN_PROVIDER_UNAVAILABLE",
        "Parsian inquiry token infrastructure is not configured.", 503);

    public static readonly Error InvalidToken = new(
        "PARSIAN_INQUIRY_TOKEN_INVALID", "Parsian inquiry returned an invalid access token.", 502);

    public static readonly Error AuthorizationRejected = new(
        "PARSIAN_INQUIRY_AUTHORIZATION_REJECTED",
        "Parsian rejected report authorization after one renewal attempt.", 502);
}
