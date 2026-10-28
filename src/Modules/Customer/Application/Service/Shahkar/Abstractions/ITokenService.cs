namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;

public interface ITokenService
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    Task<string> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    string? GetCurrentToken();
}
