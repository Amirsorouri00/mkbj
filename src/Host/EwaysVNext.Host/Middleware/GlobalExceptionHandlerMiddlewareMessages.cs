namespace EwaysVNext.Host.Middleware;

internal static class GlobalExceptionHandlerMiddlewareMessages
{
    public const string DomainExceptionLogged = "Domain exception: {Code} - {Message}";
    public const string ConcurrencyConflictLogged = "Concurrency conflict while saving aggregate";
    public const string ConcurrencyConflict = "The resource was modified by another transaction. Please retry.";
    public const string UnhandledExceptionLogged = "Unhandled exception";
    public const string UnexpectedError = "An unexpected error occurred.";
}
