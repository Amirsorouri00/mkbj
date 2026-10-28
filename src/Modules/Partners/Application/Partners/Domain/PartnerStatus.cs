namespace EwaysVNext.Modules.Partners.Application.Partners.Domain;

/// <summary>
/// Suspension is reversible and keeps the keys; it is how a partner is stopped during a billing
/// dispute or an investigation without destroying credentials they would have to redeploy.
/// Permanently ending a relationship means revoking the keys.
/// </summary>
public enum PartnerStatus
{
    Active = 0,
    Suspended = 1,
}
