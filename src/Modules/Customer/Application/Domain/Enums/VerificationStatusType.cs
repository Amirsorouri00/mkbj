namespace EwaysVNext.Modules.Customer.Application.Domain.Enums;

public enum VerificationStatusType
{
    /// <summary>
    /// Identity has not been verified
    /// </summary>
    NotVerified = 1,

    /// <summary>
    /// Identity has been verified through Shahkar
    /// </summary>
    Verified = 2,

    /// <summary>
    /// Identity has been verified manually by an expert
    /// </summary>
    VerifiedByAgent = 3,

    Pending = 4
}

