using AuthorizationService.Application.Abstractions;
using AuthorizationService.Application.Entities;
using AuthorizationService.Application.Scopes;
using AuthorizationService.Application.Scopes.Requests;
using AuthorizationService.Application.Scopes.Responses;
using EwaysVNext.Outcomes;
using Microsoft.EntityFrameworkCore;
using EwaysVNext.Outcomes.Extensions;

namespace AuthorizationService.Implementation.Services;

public class ScopeService(IApplicationDbContext db) : IScopeService
{
    public async Task<Result<ScopeDto>> CreateAsync(CreateScopeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<ScopeDto>(new Error("VALIDATION", ScopeServiceMessages.CodeAndNameRequired));
        }

        var code = request.Code.Trim();

        if (await db.Scopes.AnyAsync(s => s.Code == code, cancellationToken))
        {
            return Result.Failure<ScopeDto>(new Error("SCOPE_CODE_TAKEN", MessageFormat.Format(ScopeServiceMessages.CodeTakenFormat, code)));
        }

        var scope = Scope.Create(code, request.Name.Trim());

        db.Scopes.Add(scope);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(scope));
    }

    public async Task<Result<ScopeDto>> UpdateAsync(Guid id, UpdateScopeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<ScopeDto>(new Error("VALIDATION", ScopeServiceMessages.NameRequired));
        }

        var scope = await db.Scopes.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (scope is null)
        {
            return Result.Failure<ScopeDto>(new Error("SCOPE_NOT_FOUND", MessageFormat.Format(ScopeServiceMessages.NotFoundFormat, id)));
        }

        scope.Update(request.Name.Trim());
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(scope));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var scope = await db.Scopes.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (scope is null)
        {
            return Result.Failure(new Error("SCOPE_NOT_FOUND", MessageFormat.Format(ScopeServiceMessages.NotFoundFormat, id)));
        }

        if (await db.PermissionRuleScopes.AnyAsync(rs => rs.ScopeId == id, cancellationToken)
            || await db.UserRoleScopes.AnyAsync(a => a.ScopeId == id, cancellationToken))
        {
            return Result.Failure(new Error("SCOPE_IN_USE", MessageFormat.Format(ScopeServiceMessages.InUseFormat, id), 409));
        }

        db.Scopes.Remove(scope);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ScopeDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await db.Scopes.Where(s => s.Id == id).Select(ToDtoExpression).FirstOrDefaultAsync(cancellationToken);
        return dto is not null
            ? Result.Success(dto)
            : Result.Failure<ScopeDto>(new Error("SCOPE_NOT_FOUND", MessageFormat.Format(ScopeServiceMessages.NotFoundFormat, id)));
    }

    public async Task<Result<ScopeDto>> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var dto = await db.Scopes.Where(s => s.Code == code).Select(ToDtoExpression).FirstOrDefaultAsync(cancellationToken);
        return dto is not null
            ? Result.Success(dto)
            : Result.Failure<ScopeDto>(new Error("SCOPE_NOT_FOUND", MessageFormat.Format(ScopeServiceMessages.NotFoundFormat, code)));
    }

    public async Task<Result<IReadOnlyList<ScopeDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var list = await db.Scopes.OrderBy(s => s.Code).Select(ToDtoExpression).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ScopeDto>>(list);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Scope, ScopeDto>> ToDtoExpression =
        s => new ScopeDto(s.Id, s.Code, s.Name);

    private static ScopeDto ToDto(Scope scope) => new(scope.Id, scope.Code, scope.Name);
}
