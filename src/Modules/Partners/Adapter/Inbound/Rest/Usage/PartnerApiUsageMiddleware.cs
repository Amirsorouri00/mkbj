using System.Diagnostics;

using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
using EwaysVNext.Modules.Partners.Application.Usage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Partners.Adapter.Inbound.Rest.Usage;

/// <summary>
/// Records one row per authenticated partner request.
/// <para>
/// It runs after authentication (there is no partner to attribute the call to before that) and
/// before the rate limiter, so a 429 is logged too — "we refused you 4,000 times" is precisely
/// what a partner asks about, and a limiter that silently drops its own evidence is no use in
/// that conversation.
/// </para>
/// <para>
/// The path is recorded without its query string and truncated: a query string carries caller
/// data this table has no business keeping, and an unbounded path is an unbounded column.
/// </para>
/// </summary>
public sealed class PartnerApiUsageMiddleware(
    RequestDelegate next,
    IApiKeyUsageRecorder recorder,
    IClock clock,
    ILogger<PartnerApiUsageMiddleware> logger)
{
    private const int MaxPathLength = 512;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = Stopwatch.GetTimestamp();

        try
        {
            await next(context);
        }
        finally
        {
            // In a finally so a failed request is still recorded: an endpoint that throws is the
            // one a partner most wants evidence of.
            Record(context, started);
        }
    }

    private void Record(HttpContext context, long started)
    {
        if (!PartnerPrincipal.TryFrom(context.User, out var principal))
            return;

        var path = context.Request.Path.Value ?? string.Empty;

        var entry = new ApiKeyUsageEntry(
            principal!.PartnerId,
            principal.ApiKeyId,
            clock.UtcNow,
            context.Request.Method,
            path.Length > MaxPathLength ? path[..MaxPathLength] : path,
            context.Response.StatusCode,
            context.Response.Headers["X-Correlation-Id"].ToString(),
            (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        if (!recorder.TryRecord(entry))
        {
            // Said out loud rather than swallowed: a gap in the usage log that nobody announced
            // looks like the partner never called.
            logger.LogWarning(
                "Dropped a usage entry for partner {PartnerId}: the buffer is full.",
                principal.PartnerId);
        }
    }
}
