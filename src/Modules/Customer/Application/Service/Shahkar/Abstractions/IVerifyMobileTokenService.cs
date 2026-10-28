namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;

public interface IVerifyMobileTokenService
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    string? GetCurrentToken();
}
