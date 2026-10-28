using EwaysVNext.Domain.Common;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using EwaysVNext.Presentation.Endpoint;

namespace EwaysVNext.Host.Middleware;

/// <summary>
/// Turns anything that escapes a handler into the one error body (spec §6.3). A transient
/// infrastructure fault becomes a retryable 503 rather than an opaque 500, so a caller knows
/// resending is worth trying.
/// </summary>
public sealed class GlobalExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlerMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (DomainException dex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning(GlobalExceptionHandlerMiddlewareMessages.DomainExceptionLogged, dex.Code, dex.Message);
            await ErrorResponse.WriteAsync(ctx, new Error(dex.Code, dex.Message, 400));
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning(GlobalExceptionHandlerMiddlewareMessages.ConcurrencyConflictLogged);
            await ErrorResponse.WriteAsync(ctx, Error.ConcurrencyConflict);
        }
        catch (Exception ex) when (TransientFailure.Is(ex))
        {
            logger.LogError(ex, GlobalExceptionHandlerMiddlewareMessages.UnhandledExceptionLogged);
            await ErrorResponse.WriteAsync(ctx, Error.ServiceUnavailable);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, GlobalExceptionHandlerMiddlewareMessages.UnhandledExceptionLogged);
            await ErrorResponse.WriteAsync(
                ctx,
                Error.Internal,
                // The exception is a debugging aid, never a production response: it would leak
                // internals and stack traces to callers.
                details: environment.IsDevelopment()
                    ? new
                    {
                        type = ex.GetType().FullName,
                        message = ex.Message,
                        stackTrace = ex.StackTrace,
                    }
                    : null);
        }
    }
}
