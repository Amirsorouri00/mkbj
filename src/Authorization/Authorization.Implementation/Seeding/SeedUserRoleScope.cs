namespace AuthorizationService.Implementation.Seeding;

public class SeedUserRoleScope
{
    public Guid UserId { get; set; }
    public string RoleCode { get; set; } = default!;
    public string ScopeCode { get; set; } = default!;
}
