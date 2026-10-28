using Customer.Infrastructure.Shahkar.DTOs;

namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;

public interface IAuthApi
{
    [Refit.Post("/management/api/v1/LegalPerson/AuthorizeByUserPassApi")]
    [Refit.Headers("Content-Type: application/json-patch+json")]
    Task<ApiResponse<ShahkarTokenResponse>> AuthorizeAsync([Refit.Body] ShahkarTokenRequest request);

    [Refit.Post("/management/api/v1/LegalPerson/RenewToken")]
    [Refit.Headers("Content-Type: application/json-patch+json")]
    Task<ApiResponse<ShahkarTokenResponse>> RenewTokenAsync(
        [Refit.Header("Authorization")] string authorization,
        [Refit.Body] object request); 
}

