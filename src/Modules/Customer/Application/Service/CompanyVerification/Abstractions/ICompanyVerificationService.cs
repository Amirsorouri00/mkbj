using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Customer.Application.Service.CompanyVerification.Abstractions;

public interface ICompanyVerificationService
{
    Task<Result> VerifyAsync(CompanyVerificationRequest request, CancellationToken ct = default);
}

public sealed record CompanyVerificationRequest(
    string CompanyName,
    string NationalId,
    DateOnly RegistrationDate,
    string RegistrationNumber,
    string CeoName);
