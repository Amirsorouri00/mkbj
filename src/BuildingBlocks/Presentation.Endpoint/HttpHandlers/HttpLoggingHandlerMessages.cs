namespace EwaysVNext.Presentation.Endpoint.HttpHandlers;

internal static class HttpLoggingHandlerMessages
{
    public const string RequestCompleted = "{ModuleName}/{ServiceName} {Method} {Url} -> {StatusCode} in {ElapsedMs}ms. Request: {RequestBody} Response: {Response}";
    public const string RequestFailed = "{ModuleName}/{ServiceName} {Method} {Url} failed after {ElapsedMs}ms. Request: {RequestBody}";
}
