using Customer.Infrastructure.Shahkar.DTOs;

namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;


public interface IVerificationApi
{
    [Refit.Post("/api/v1/IdentityDataTypeG")]
    [Refit.Headers("Content-Type: application/json")]
    Task<ApiResponse<ShahkarIdentityResponse>> VerifyCitizenshipAsync(
        [Refit.Header("Authorization")] string authorization,
        [Refit.Body] ShahkarIdentityRequest request);
}


