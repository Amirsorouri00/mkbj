using AuthorizationService.Application.UserRoleScopes.Requests;
using AuthorizationService.Application.UserRoleScopes.Responses;
using EwaysVNext.Outcomes;

namespace AuthorizationService.Application.UserRoleScopes;

public interface IUserRoleScopeService
{
    Task<Result<UserRoleScopeDto>> AssignAsync(AssignUserRoleScopeRequest request, CancellationToken cancellationToken = default);
    Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<UserRoleScopeDto>>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<UserRoleScopeDto>> BootstrapAdminAsync(Guid userId, Guid scopeId, CancellationToken cancellationToken = default);
}
