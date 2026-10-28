namespace AuthorizationService.Application.Entities;

public class PermissionRuleScope
{
    public Guid PermissionRuleId { get; set; }
    public PermissionRule PermissionRule { get; set; } = default!;

    public Guid ScopeId { get; set; }
    public Scope Scope { get; set; } = default!;
}
