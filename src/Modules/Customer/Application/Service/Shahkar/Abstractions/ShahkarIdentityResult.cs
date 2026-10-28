namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;
public class ShahkarIdentityResult
{
    /// <summary>
    /// Whether the identity was verified as valid
    /// </summary>
    public bool Valid { get; set; }

    /// <summary>
    /// Reference ID from Shahkar for tracking
    /// </summary>
    public string? Reference { get; set; }

    /// <summary>
    /// First name from Shahkar database
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Last name from Shahkar database
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// Father name from Shahkar database
    /// </summary>
    public string? FatherName { get; set; }

    /// <summary>
    /// Gender from Shahkar database
    /// </summary>
    public string? Gender { get; set; }

    /// <summary>
    /// Liveness status
    /// </summary>
    public bool? Liveness { get; set; }

    /// <summary>
    /// FID from Shahkar
    /// </summary>
    public string? Fid { get; set; }

    /// <summary>
    /// Error message if verification failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}
