namespace EwaysVNext.Modules.Customer.Application.Service;

internal static class TeamMemberServiceMessages
{
    public const string TeamMemberNotFound = "Team member was not found.";
    public const string MobileNumberAlreadyExists = "Mobile number already exists.";
    public const string NationalCodeAlreadyExists = "National ID already exists.";
    public const string PersonnelCodeAlreadyExists = "Personnel code already exists.";
    public const string SelfDeleteNotAllowed = "You cannot delete your own account.";
    public const string PersistenceConflict = "The request conflicts with existing data. Please retry.";
    public const string PersistAfterProvisioningFailed = "Failed to persist TeamMember after Keycloak provisioning succeeded (IdentityUserId: {IdentityUserId}). Compensating.";
    public const string PersistProfileUpdateFailed = "Failed to persist TeamMember profile update for id {Id}.";
}
