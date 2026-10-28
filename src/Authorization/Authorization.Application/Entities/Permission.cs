namespace AuthorizationService.Application.Entities;

public class Permission
{
    private readonly List<PermissionRule> _rules = new();

    private Permission()
    {
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<PermissionRule> Rules => _rules;

    public static Permission Create(string code, string name, string? description, IEnumerable<PermissionRuleInput> rules)
    {
        var now = DateTime.UtcNow;
        var permission = new Permission
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Description = description,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        permission.ReplaceRules(rules);

        return permission;
    }

    public void Update(string name, string? description, IEnumerable<PermissionRuleInput> rules)
    {
        Name = name;
        Description = description;
        ReplaceRules(rules);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void ReplaceRules(IEnumerable<PermissionRuleInput> rules)
    {
        var ruleList = rules.ToList();
        if (ruleList.Count == 0)
        {
            throw new InvalidPermissionRuleException(PermissionMessages.AtLeastOneRuleRequired);
        }

        _rules.Clear();
        foreach (var rule in ruleList)
        {
            _rules.Add(PermissionRule.Create(Id, rule.RoleId, rule.ScopeIds));
        }
    }
}
