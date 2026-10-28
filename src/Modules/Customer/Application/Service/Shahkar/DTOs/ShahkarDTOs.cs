using System.Text.Json.Serialization;

namespace Customer.Infrastructure.Shahkar.DTOs;

/// <summary>
/// Request DTO for Shahkar token endpoint
/// </summary>
public class ShahkarTokenRequest
{
    [JsonPropertyName("Username")]
    public string Username { get; set; } = string.Empty;
    [JsonPropertyName("Password")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Response DTO from Shahkar token endpoint
/// </summary>
public class ShahkarTokenResponse
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }
    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }
    [JsonPropertyName("tokenType")]
    public string? TokenType { get; set; }
    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

/// <summary>
/// Request DTO for Shahkar identity verification (IdentityDataTypeG)
/// </summary>
public class ShahkarIdentityRequest
{
    /// <summary>
    /// National code (10 digits)
    /// </summary>
    [JsonPropertyName("NationalCode")]
    public string NationalCode { get; set; } = string.Empty;

    /// <summary>
    /// Birth date in Shamsi format (YYYY/MM/DD)
    /// </summary>
    [JsonPropertyName("birthDate")]
    public string BirthDate { get; set; } = string.Empty;
}

/// <summary>
/// Response DTO from Shahkar identity verification endpoint
/// </summary>
public class ShahkarIdentityResponse
{
    /// <summary>
    /// Whether the identity is valid
    /// </summary>
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    /// <summary>
    /// Person information
    /// </summary>
    [JsonPropertyName("personInfo")]
    public ShahkarPersonInfo? PersonInfo { get; set; }

    [JsonPropertyName("liveness")]
    public bool Liveness { get; set; }

    [JsonPropertyName("fid")]
    public string Fid { get; set; } = string.Empty;
}

/// <summary>
/// Person information from Shahkar
/// </summary>
public class ShahkarPersonInfo
{
    /// <summary>
    /// National ID
    /// </summary>
    [JsonPropertyName("nationalId")]
    public string? NationalId { get; set; }

    /// <summary>
    /// First name
    /// </summary>
    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    /// <summary>
    /// Last name
    /// </summary>
    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    /// <summary>
    /// Father name
    /// </summary>
    [JsonPropertyName("fatherName")]
    public string? FatherName { get; set; }

    /// <summary>
    /// Gender
    /// </summary>
    [JsonPropertyName("gender")]
    public string? Gender { get; set; }
}
public class VerifyMobileRequest
{
    [JsonPropertyName("mobile")]
    public string Mobile { get; set; } = string.Empty;

    [JsonPropertyName("nationalCode")]
    public string NationalCode { get; set; } = string.Empty;
}
public class AuthVerifyMobileRequest
{
    [JsonPropertyName("grant_type")]
    public string grant_type { get; set; } = string.Empty;

    [JsonPropertyName("client_id")]
    public string client_id { get; set; } = string.Empty;

    [JsonPropertyName("Client_secret")]
    public string Client_secret { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}
public class AuthVerifyMobileResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("expires_in")]
    public string ExpiresIn { get; set; } = string.Empty;
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;
}
public class VerifyMobileResponse
{
    [JsonPropertyName("data")]
    public bool? Data { get; set; }

    [JsonPropertyName("meta")]
    public string Meta { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public ShahkarErrorResponse Error { get; set; } = new();
    public class ShahkarErrorResponse
    {
        [JsonPropertyName("errorCode")]
        public string ErrorCode { get; set; } = string.Empty;

        [JsonPropertyName("customMessage")]
        public string CustomMessage { get; set; } = string.Empty;

        [JsonPropertyName("exception")]
        public string Exception { get; set; } = string.Empty;
    }
}

