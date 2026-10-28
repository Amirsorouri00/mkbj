namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;

using global::Customer.Infrastructure.Shahkar.DTOs;
using Refit;

public interface IVerifyMobileApi
{
    [Post("/hub/api/v1/Shahkar/MixVerifyMobile")]
    [Headers("Content-Type: application/json")]
    Task<VerifyMobileResponse> VerifyMobileNationalCodeAsync(
        [Header("Authorization")] string authorization,
        [Body] VerifyMobileRequest request);

}

