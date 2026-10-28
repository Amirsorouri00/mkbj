namespace EwaysVNext.Modules.Customer.Application.Domain.Enums;

public enum VerificationMethodType
{
    /// <summary>
    /// No verification method has been used
    /// </summary>
    None = 1,

    /// <summary>
    /// Identity was verified through Shahkar service
    /// </summary>
    Shahkar = 2,

    /// <summary>
    /// Identity was manually verified by an expert
    /// </summary>
    Agent = 3
}
