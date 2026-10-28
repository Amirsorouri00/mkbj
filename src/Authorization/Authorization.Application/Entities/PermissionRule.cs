namespace AuthorizationService.Application.Entities;

public class PermissionRule
{
    private readonly List<PermissionRuleScope> _scopes = new();

    private PermissionRule()
    {
    }

    public Guid Id { get; private set; }
    public Guid PermissionId { get; private set; }
    public Permission Permission { get; private set; } = default!;
    public Guid? RoleId { get; private set; }
    public Role? Role { get; private set; }

    public IReadOnlyCollection<PermissionRuleScope> Scopes => _scopes;

    internal static PermissionRule Create(Guid permissionId, Guid? roleId, IEnumerable<Guid> scopeIds)
    {
        var scopes = scopeIds.Distinct().ToList();

        if (roleId is null && scopes.Count == 0)
        {
            throw new InvalidPermissionRuleException(PermissionRuleMessages.RoleOrScopeRequired);
        }

        var rule = new PermissionRule
        {
            Id = Guid.NewGuid(),
            PermissionId = permissionId,
            RoleId = roleId,
        };

        foreach (var scopeId in scopes)
        {
            rule._scopes.Add(new PermissionRuleScope
            {
                PermissionRuleId = rule.Id,
                ScopeId = scopeId,
            });
        }

        return rule;
    }
}
