namespace EwaysVNext.Modules.Customer.Application.Domain.TeamMembers;

internal static class TeamMemberMessages
{
    public const string IdentityUserIdRequired = "A provisioned Keycloak user id is required.";
    public const string DeletedCannotBeEdited = "A deleted team member cannot be edited.";
    public const string AlreadyDeleted = "This team member has already been deleted.";
    public const string FirstNameRequired = "First name is required.";
    public const string FirstNameTooLong = "First name cannot exceed 100 characters.";
    public const string LastNameRequired = "Last name is required.";
    public const string LastNameTooLong = "Last name cannot exceed 100 characters.";
    public const string DateOfBirthRequired = "Date of birth is required.";
    public const string DateOfBirthCannotBeFuture = "Date of birth cannot be in the future.";
    public const string PersonnelCodeRequired = "Personnel code is required.";
    public const string PersonnelCodeTooLong = "Personnel code cannot exceed 50 characters.";
    public const string DepartmentRequired = "Department is required.";
    public const string DepartmentTooLong = "Department cannot exceed 100 characters.";
    public const string JobTitleRequired = "Job title is required.";
    public const string JobTitleTooLong = "Job title cannot exceed 100 characters.";
    public const string ReviewingTeamMemberNotFound = "The reviewing team member could not be identified.";
}
