using Customer.Infrastructure.Shahkar.DTOs;
using EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar;

public class VerifyMobileTokenService(IAuthVerifyMobileApi authApi, IOptions<ShahkarOptions> apiSettings) : IVerifyMobileTokenService
{
    private readonly ShahkarOptions _apiSettings = apiSettings.Value;
    private string? _cachedAccessToken;
    private DateTime _tokenExpiry;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_cachedAccessToken) && _tokenExpiry > DateTime.Now)
            return _cachedAccessToken;

        var request = new AuthVerifyMobileRequest
        {
            grant_type = _apiSettings.GrantType,
            client_id = _apiSettings.ClientId,
            Client_secret = _apiSettings.ClientSecret,
            Username = _apiSettings.VerifyMobileUsername,
            Password = _apiSettings.VerifyMobilePassword
        };

        var response = await authApi.AuthorizeAsync(request);

        if (string.IsNullOrEmpty(response?.AccessToken))
            throw new InvalidOperationException(VerifyMobileTokenServiceMessages.GetTokenError);
        if (int.TryParse(response.ExpiresIn, out int ext))
        {
            _tokenExpiry = DateTime.Now.AddSeconds((ext / 60));
        }
        else
        {
            _tokenExpiry = DateTime.Now.AddMinutes(60);
        }
        _cachedAccessToken = response.AccessToken;
        return _cachedAccessToken;
    }

    public string? GetCurrentToken() => _cachedAccessToken;
}
