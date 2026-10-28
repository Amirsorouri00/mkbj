using AuthorizationService.Abstractions.Authorization.Requests;
using AuthorizationService.Abstractions.Authorization.Responses;

namespace AuthorizationService.Abstractions.Authorization;

public interface IAuthorizationCheckService
{
    Task<CheckAuthorizationResult> CheckAsync(CheckAuthorizationRequest request, CancellationToken cancellationToken = default);
}
