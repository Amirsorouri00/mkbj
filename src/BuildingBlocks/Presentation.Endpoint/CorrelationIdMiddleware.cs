using System.Text.RegularExpressions;
using EwaysVNext.Core.Application;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace EwaysVNext.Presentation.Endpoint;

/// <summary>
/// Gives every request a correlation id, echoes it on the response and puts it on the log scope.
/// <para>
/// A caller-supplied id is accepted only if it matches a conservative character set and length.
/// It is written into a response header and into logs, so an unvalidated value is a header- and
/// log-injection vector; anything that does not match is replaced rather than sanitized.
/// </para>
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation)
    {
        var incoming = context.Request.Headers[CorrelationHeaders.CorrelationId].ToString();
        var correlationId = AcceptableId().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        correlation.CorrelationId = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeaders.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9\-_.:]{1,128}$")]
    private static partial Regex AcceptableId();
}
