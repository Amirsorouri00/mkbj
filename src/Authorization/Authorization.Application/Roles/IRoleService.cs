using AuthorizationService.Application.Roles.Requests;
using AuthorizationService.Application.Roles.Responses;
using EwaysVNext.Outcomes;

namespace AuthorizationService.Application.Roles;

public interface IRoleService
{
    Task<Result<RoleDto>> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken = default);
    Task<Result<RoleDto>> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<RoleDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<RoleDto>> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<RoleDto>>> ListAsync(CancellationToken cancellationToken = default);
}
