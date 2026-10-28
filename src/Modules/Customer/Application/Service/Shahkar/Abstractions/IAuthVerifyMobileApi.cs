namespace EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;

using global::Customer.Infrastructure.Shahkar.DTOs;
using Refit;

public interface IAuthVerifyMobileApi
{
    [Post("/sts/connect/token")]
    [Headers("Content-Type: application/x-www-form-urlencoded")]
    Task<AuthVerifyMobileResponse> AuthorizeAsync(
        [Body] AuthVerifyMobileRequest request);
}

