using Customer.Infrastructure.Shahkar.DTOs;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;

public interface IShahkarService
{
    Task<Result<ApiResponse<ShahkarIdentityResponse>>> VerifyCitizenshipAsync(string nationalCode, DateOnly birthDate, CancellationToken ct);
    Task<Result<VerifyMobileResponse>> VerifyMobileNationalCodeAsync(string mobile,string nationalCode, CancellationToken ct);
    Task<Result<CompanyInquiryResponse>> GetCompanyInquiryAsync(string companyId, CancellationToken ct);
}
