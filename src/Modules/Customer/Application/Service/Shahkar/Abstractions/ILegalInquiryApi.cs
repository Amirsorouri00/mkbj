namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;

using global::Customer.Infrastructure.Shahkar.DTOs;
using Refit;

public interface ILegalInquiryApi
{
    [Get("/hub/api/v1/LegalInquiry/GetCompanyInquiry")]
    [Headers("Content-Type: application/json")]
    Task<CompanyInquiryResponse> GetCompanyInquiryAsync(
        [Header("Authorization")] string authorization,
        [AliasAs("CompanyId")] string companyId,
        CancellationToken cancellationToken);
}
