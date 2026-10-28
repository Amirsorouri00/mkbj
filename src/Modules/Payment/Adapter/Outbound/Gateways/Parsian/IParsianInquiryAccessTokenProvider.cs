using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

internal interface IParsianInquiryAccessTokenProvider
{
    Task<Result<string>> GetValidTokenAsync(CancellationToken cancellationToken);
    Task<Result<string>> RenewRejectedTokenAsync(string rejectedToken, CancellationToken cancellationToken);
}
