namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

internal static class CustomerMessages
{
    public const string KeycloakUserIdRequired = "Keycloak user ID is required.";
    public const string MobileNumberRequired = "mobile number is required.";
    public const string FirstNameRequired = "First name is required.";
    public const string FirstNameTooLong = "first name cannot exceed 100 characters.";
    public const string LastNameRequired = "Last name is required.";
    public const string LastNameTooLong = "Last name cannot exceed 100 characters.";
    public const string InvalidBirthDateFormat = "Birth date must be in ISO 8601 format (YYYY-MM-DD).";
    public const string BirthDateCannotBeFuture = "Birth date cannot be in the future.";
    public const string EconomicCodeTooLong = "Economic code cannot exceed 10 characters.";
    public const string IntaCodeTooLong = "INTA code cannot exceed 20 characters.";
    public const string RoleCodeTooLong = "Role code cannot exceed 20 characters.";
    public const string EditNotAllowedWhileAwaitingExpertCall =
        "The customer's information cannot be edited while awaiting the expert's call; only the registration history and registration status are available.";
    public const string InvalidGender = "Gender is not valid.";
    public const string SecondaryPhoneNumberSameAsMobile = "The second contact number must differ from the customer's mobile number.";
    public const string CannotUnverifyApprovedCustomer = "An approved customer's verification cannot be downgraded below Verified.";
    public const string VerificationRequiredForApproval = "Customer identity must be verified before approval.";
    public const string CustomerNotFound = "Customer was not found.";
    public const string CustomerAlreadyVerifyChecked = "The customer has already been verify checked";
    public const string JobTypeNotFound = "One or more of the specified job types do not exist.";
}
