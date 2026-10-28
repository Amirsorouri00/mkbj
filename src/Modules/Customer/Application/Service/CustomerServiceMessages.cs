namespace EwaysVNext.Modules.Customer.Application.Service;

internal static class CustomerServiceMessages
{
    public const string ShahkarFailed = "Please wait for Done Manual Verification.";
    public const string ShahkarDoesntMatch = "The information entered doesn't match your mobile number";
    public const string InvalidPostalCode = "Postal code must be exactly 10 digits.";
    public const string CityNotInProvince = "The selected city does not belong to the selected province.";
    public const string MobileVerificationStarted = "Calling Shahkar mobile-national code verification. CustomerId: {CustomerId}";
    public const string MobileVerificationCompleted = "Shahkar mobile-national code verification completed. CustomerId: {CustomerId}, IsSuccess: {IsSuccess}, Data: {Data}, ErrorCode: {ErrorCode}";
    public const string CitizenshipVerificationStarted = "Calling Shahkar citizenship verification. nationalCode: {nationalCode}";
    public const string CitizenshipVerificationCompleted = "Shahkar citizenship verification completed. nationalCode: {nationalCode}, IsSuccess: {IsSuccess}, Valid: {Valid}, Liveness: {Liveness}, ErrorCode: {ErrorCode}";
    public const string MobileVerificationFailed = "Shahkar mobile-national code verification failed. CustomerId: {CustomerId}";
    public const string CitizenshipVerificationFailed = "Shahkar citizenship verification failed. CustomerId: {CustomerId}, Fid: {Fid}";
    public const string VerificationPending = "Shahkar verification is pending. CustomerId: {CustomerId}";
}
