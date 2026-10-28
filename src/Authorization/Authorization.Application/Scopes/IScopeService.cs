using AuthorizationService.Application.Scopes.Requests;
using AuthorizationService.Application.Scopes.Responses;
using EwaysVNext.Outcomes;

namespace AuthorizationService.Application.Scopes;

public interface IScopeService
{
    Task<Result<ScopeDto>> CreateAsync(CreateScopeRequest request, CancellationToken cancellationToken = default);
    Task<Result<ScopeDto>> UpdateAsync(Guid id, UpdateScopeRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<ScopeDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<ScopeDto>> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ScopeDto>>> ListAsync(CancellationToken cancellationToken = default);
}
