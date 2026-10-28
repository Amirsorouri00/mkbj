using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

public sealed class CustomerVerificationDetails
{
    public long Id { get; private set; }
    public long VerificationId { get;private set; }
    /// <summary>
    /// First name returned by Shahkar.
    /// </summary>
    public string Firstname { get; private set; } = string.Empty;

    /// <summary>
    /// Last name returned by Shahkar.
    /// </summary>
    public string Lastname { get; private set; } = string.Empty;

    /// <summary>
    /// Father name returned by Shahkar.
    /// </summary>
    public string Fathername { get; private set; } = string.Empty;

    /// <summary>
    /// Gender returned by Shahkar.
    /// </summary>
    public Gender Gender { get; private set; }

    /// <summary>
    /// Liveness result returned by Shahkar.
    /// </summary>
    public bool Liveness { get; private set; }

    /// <summary>
    /// Associated customer verification.
    /// </summary>
    public CustomerVerification? Verification { get; private set; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; private set; }
    

    private CustomerVerificationDetails()
    {
    }

    internal static CustomerVerificationDetails Create(
        long id,
        long verificationId,
        string? firstName,
        string? lastName,
        string? fatherName,
        Gender gender,
        bool liveness,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException(CustomerVerificationDetailsMessages.FirstNameRequired, nameof(firstName));

        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException(CustomerVerificationDetailsMessages.LastNameRequired, nameof(lastName));

        if (string.IsNullOrWhiteSpace(fatherName))
            throw new ArgumentException(CustomerVerificationDetailsMessages.FatherNameRequired, nameof(fatherName));

        return new CustomerVerificationDetails
        {
            Id = id,
            VerificationId = verificationId,
            Firstname = firstName,
            Lastname = lastName,
            Fathername = fatherName,
            Gender = gender,
            Liveness = liveness,
            CreatedAt = now.UtcDateTime
        };
    }
}
