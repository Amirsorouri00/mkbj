namespace AuthorizationService.Application.Entities;

public class UserRoleScope
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = default!;
    public Guid ScopeId { get; set; }
    public Scope Scope { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
}
