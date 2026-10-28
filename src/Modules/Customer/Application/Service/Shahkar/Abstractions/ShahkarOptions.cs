namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;
/// <summary>
/// Configuration options for Shahkar service
/// </summary>
public class ShahkarOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ApiVerficationMobileUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public string GrantType { get; internal set; } = string.Empty;
    public string ClientId { get; internal set; } = string.Empty;
    public string ClientSecret { get; internal set; } = string.Empty;
    public string VerifyMobileUsername { get; internal set; } = string.Empty;
    public string VerifyMobilePassword { get; internal set; } = string.Empty;
}
