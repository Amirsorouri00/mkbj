using System.Text.Json.Serialization;

namespace EwaysVNext.Host.Authentication;

public sealed record KeycloakRealmTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("refresh_expires_in")] int? RefreshExpiresIn,
    [property: JsonPropertyName("token_type")] string? TokenType);
