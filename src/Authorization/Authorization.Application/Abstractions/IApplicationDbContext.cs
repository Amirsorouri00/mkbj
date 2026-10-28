using AuthorizationService.Application.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuthorizationService.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<Permission> Permissions { get; }
    DbSet<PermissionRule> PermissionRules { get; }
    DbSet<PermissionRuleScope> PermissionRuleScopes { get; }
    DbSet<UserRoleScope> UserRoleScopes { get; }
    DbSet<Role> Roles { get; }
    DbSet<Scope> Scopes { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
