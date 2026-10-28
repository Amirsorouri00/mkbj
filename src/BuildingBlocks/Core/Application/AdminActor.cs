using EwaysVNext.Audit;

namespace EwaysVNext.Core.Application;

/// <summary>
/// The authenticated backoffice user behind an admin action (spec §6.1). Two roles only: staff,
/// and super admin, who alone may touch partners, FX, currencies, settings and refunds.
/// </summary>
public sealed record AdminActor(Guid UserId, bool IsSuperAdmin)
{
    public AuditActor ToAudit() => new(UserId, IsSuperAdmin ? "super_admin" : "staff");
}
