using Customer.Infrastructure.Shahkar.DTOs;
using EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar;

public class TokenService(IAuthApi authApi, IOptions<ShahkarOptions> apiSettings) : ITokenService
{
    private readonly ShahkarOptions _apiSettings = apiSettings.Value;
    private string? _cachedAccessToken;
    private string? _cachedRefreshToken;
    private DateTime _tokenExpiry;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_cachedAccessToken) && _tokenExpiry > DateTime.Now)
            return _cachedAccessToken;

        if (!string.IsNullOrEmpty(_cachedRefreshToken) && _tokenExpiry <= DateTime.Now)
        {
            try
            {
                return await RefreshTokenAsync(_cachedRefreshToken, cancellationToken);
            }
            catch
            {
                
            }
        }

        var request = new ShahkarTokenRequest
        {
            Username = _apiSettings.Username,
            Password = _apiSettings.Password
        };

        var response = await authApi.AuthorizeAsync(request);

        if (response?.Data == null)
            throw new InvalidOperationException(TokenServiceMessages.GetTokenError);

        _cachedAccessToken = response.Data.AccessToken;
        _cachedRefreshToken = response.Data.RefreshToken;
        _tokenExpiry = DateTime.Now.AddSeconds(response.Data.ExpiresIn - 60);

        return _cachedAccessToken!;
    }

    public async Task<string> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var payload = new { refreshToken };
        var authorization = $"Bearer {_cachedAccessToken}";

        var response = await authApi.RenewTokenAsync(authorization, payload);

        if (response?.Data == null)
            throw new InvalidOperationException(TokenServiceMessages.GetRefreshTokenError);

        _cachedAccessToken = response.Data.AccessToken;
        _cachedRefreshToken = response.Data.RefreshToken;
        _tokenExpiry = DateTime.Now.AddSeconds(response.Data.ExpiresIn - 60);

        return _cachedAccessToken!;
    }

    public string? GetCurrentToken() => _cachedAccessToken;
}
