using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;

namespace EwaysVNext.Modules.Customer.Application.Ports.Inbound;

public interface ICustomerInboundPort
{
    // Profile lifecycle (REST-facing)
    Task<Result<long>> RegisterAsync(Guid identityUserId,
    string mobileNumber,
    CancellationToken ct = default);

    Task<Result> RegisterCustomerInfoAsync(Guid identityUserId,
    string nationalCode,
    string firstName,
    string lastName,
    Gender gender,
    DateOnly birthDate,
    int cityId,
    int? districtId,
    string? address,
    CancellationToken ct = default);
    Task<Result> UpdateProfileAsync(Guid identityUserId,
    string firstName,
    string lastName,
    Gender gender,
    DateOnly birthDate,
    CancellationToken ct = default);

    Task<Result<CustomerProfile>> GetProfileAsync(
        Guid identityUserId,
        CancellationToken ct = default);

    // Admin-facing status management (GraphQL-facing)
    Task<Result> UpdateStatusAsync(
        long customerId,
        CustomerStatusType status,
        Guid reviewingIdentityUserId,
        string? reason = null,
        CancellationToken ct = default);

    Task<Result> UpdateCustomerByAdminAsync(
        long customerId,
        AdminCustomerUpdate update,
        CancellationToken ct = default);

    Task<Result> UpdateVerificationStatusAsync(
        long customerId,
        int expertId,
        VerificationStatusType status,
        Guid reviewingIdentityUserId,
        string? reason = null,
        CancellationToken ct = default);
}
