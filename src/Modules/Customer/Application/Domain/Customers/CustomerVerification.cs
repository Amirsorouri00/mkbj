using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

public sealed class CustomerVerification
{
    public long Id { get; private set; }
    public long CustomerId { get; private set; }

    public Customer? Customer { get; private set; }

    public string NationalId { get; private set; } = string.Empty;

    public DateOnly BirthDate { get; private set; }

    public int Status { get; private set; }

    public int? VerificationMethod { get; private set; }

    public string? ShahkarReference { get; private set; }

    public int? ExpertId { get; private set; }

    public bool NationalCardReceived { get; private set; }

    public DateTime? NationalCardReviewedAt { get; private set; }

    public DateTime? VerifiedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    public string? Reason { get; private set; }

    public VerificationMethod VerificationMethodNavigation { get; private set; } = null!;
    public CustomerVerificationDetails? Details { get; private set; }

    private CustomerVerification()
    {
    }

    public static CustomerVerification Create(
        long id,
        long customerId,
        string nationalId,
        DateOnly birthDate,
        DateTimeOffset now)
    {
        if (customerId <= 0)
            throw new DomainException("CUSTOMER_REQUIRED", CustomerVerificationMessages.CustomerRequired);

        if (string.IsNullOrWhiteSpace(nationalId))
            throw new DomainException("NATIONALID_REQUIRED", CustomerVerificationMessages.NationalIdRequired);

        var createdAt = now.UtcDateTime;

        return new CustomerVerification
        {
            Id = id,
            CustomerId = customerId,
            NationalId = nationalId,
            BirthDate = birthDate,
            Status = (int)VerificationStatusType.NotVerified,
            VerificationMethod = null,
            NationalCardReceived = false,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    public void MarkAsShahkarVerified(
        long detailsId,
        string? firstName,
        string? lastName,
        string? fatherName,
        Gender gender,
        bool liveness,
        DateTimeOffset now,
        string? shahkarReference = null)
    {
        Details = CustomerVerificationDetails.Create(
            id: detailsId,
            verificationId: Id,
            firstName: firstName,
            lastName: lastName,
            fatherName: fatherName,
            gender: gender,
            liveness: liveness,
            now: now);

        Status = (int)VerificationStatusType.Verified;
        VerificationMethod = (int)VerificationMethodType.Shahkar;
        ShahkarReference = shahkarReference;
        VerifiedAt = now.UtcDateTime;
        UpdatedAt = now.UtcDateTime;
    }

    public void MarkAsExpertVerified(int expertId, DateTimeOffset now)
    {
        if (expertId <= 0)
            throw new DomainException("EXPERTCODE_REQUIRED", CustomerVerificationMessages.ExpertCodeRequired);

        Status = (int)VerificationStatusType.VerifiedByAgent;
        VerificationMethod = (int)VerificationMethodType.Agent;
        ExpertId = expertId;
        VerifiedAt = now.UtcDateTime;
        UpdatedAt = now.UtcDateTime;
    }

    public void MarkAsNotVerified(string? shahkarReference, DateTimeOffset now)
    {
        Status = (int)VerificationStatusType.NotVerified;
        VerificationMethod = (int)VerificationMethodType.Shahkar;
        ShahkarReference = shahkarReference;
        VerifiedAt = null;
        UpdatedAt = now.UtcDateTime;
    }
    public void MarkAsPendding(DateTimeOffset now)
    {
        Status = (int)VerificationStatusType.Pending;
        VerificationMethod = (int)VerificationMethodType.Shahkar;
        ShahkarReference = null;
        VerifiedAt = null;
        UpdatedAt = now.UtcDateTime;
    }

    public void RecordRejection(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(CustomerVerificationErrorCodes.RejectionReasonRequired, CustomerVerificationMessages.RejectionReasonRequired);

        Reason = reason;
        VerifiedAt = now.UtcDateTime;
        UpdatedAt = now.UtcDateTime;
    }

    public void ClearRejectionReason(DateTimeOffset now)
    {
        if (Reason is null)
            return;

        Reason = null;
        UpdatedAt = now.UtcDateTime;
    }
}
