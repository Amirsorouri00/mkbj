using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Customer.Application.Ports.Outbound;

public sealed record CustomerRegistrationReview(
    SnowflakeId CustomerId,
    DateTime RegistrationDate,
    DateTime? VerificationDate,
    int? StatusId,
    int? VerificationMethod,
    string? Reason);
