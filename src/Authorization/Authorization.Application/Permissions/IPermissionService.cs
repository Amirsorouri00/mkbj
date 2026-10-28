using AuthorizationService.Application.Permissions.Requests;
using AuthorizationService.Application.Permissions.Responses;
using EwaysVNext.Outcomes;

namespace AuthorizationService.Application.Permissions;

public interface IPermissionService
{
    Task<Result<PermissionDto>> CreateAsync(CreatePermissionRequest request, CancellationToken cancellationToken = default);
    Task<Result<PermissionDto>> UpdateAsync(Guid id, UpdatePermissionRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<PermissionDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<PermissionDto>> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<PermissionDto>>> ListAsync(CancellationToken cancellationToken = default);
}
