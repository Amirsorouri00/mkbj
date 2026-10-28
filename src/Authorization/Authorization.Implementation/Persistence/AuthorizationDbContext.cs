using AuthorizationService.Application.Abstractions;
using AuthorizationService.Application.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuthorizationService.Implementation.Persistence;

public class AuthorizationDbContext(DbContextOptions<AuthorizationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<PermissionRule> PermissionRules => Set<PermissionRule>();
    public DbSet<PermissionRuleScope> PermissionRuleScopes => Set<PermissionRuleScope>();
    public DbSet<UserRoleScope> UserRoleScopes => Set<UserRoleScope>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Scope> Scopes => Set<Scope>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("authorization");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthorizationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
