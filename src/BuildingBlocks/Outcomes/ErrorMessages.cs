namespace EwaysVNext.Outcomes;

internal static class ErrorMessages
{
    public const string NotFound = "Resource was not found.";
    public const string Conflict = "Resource already exists.";
    public const string Validation = "Validation failed.";
    public const string Unauthorized = "Unauthorized.";
    public const string Forbidden = "Forbidden.";
    public const string Internal = "Internal server error.";
    public const string ConcurrencyConflict = "The resource was modified by another transaction. Please retry.";
    public const string PersistenceConflict = "A persistence conflict occurred.";
}
