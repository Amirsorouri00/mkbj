namespace EwaysVNext.Modules.Customer.Application.Service.Http;

internal static class ShahkarLoggingHandlerMessages
{
    public const string RequestStarted = "Shahkar HTTP Request started. Method: {Method}, Uri: {Uri}";
    public const string RequestCompleted = "Shahkar HTTP Request completed. Method: {Method}, Uri: {Uri}, StatusCode: {StatusCode}, DurationMs: {DurationMs}";
    public const string RequestCancelled = "Shahkar HTTP Request cancelled or timed out. Method: {Method}, Uri: {Uri}, DurationMs: {DurationMs}";
    public const string RequestFailed = "Shahkar HTTP Request failed. Method: {Method}, Uri: {Uri}, DurationMs: {DurationMs}";
}
