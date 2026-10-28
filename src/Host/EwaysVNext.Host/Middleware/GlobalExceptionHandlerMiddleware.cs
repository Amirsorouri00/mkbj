using System.Net;
using System.Text.Json;
using EwaysVNext.Domain.Common;

namespace EwaysVNext.Host.Middleware;

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
            await WriteAsync(ctx, HttpStatusCode.BadRequest, dex.Code, dex.Message);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning(GlobalExceptionHandlerMiddlewareMessages.ConcurrencyConflictLogged);
            await WriteAsync(ctx, HttpStatusCode.Conflict, "CONCURRENCY_CONFLICT",
                GlobalExceptionHandlerMiddlewareMessages.ConcurrencyConflict);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, GlobalExceptionHandlerMiddlewareMessages.UnhandledExceptionLogged);
            await WriteAsync(
                ctx,
                HttpStatusCode.InternalServerError,
                "INTERNAL",
                GlobalExceptionHandlerMiddlewareMessages.UnexpectedError,
                environment.IsDevelopment() ? ex : null);
        }
    }

    private static async Task WriteAsync(
        HttpContext ctx,
        HttpStatusCode status,
        string code,
        string detail,
        Exception? exception = null)
    {
        ctx.Response.StatusCode = (int)status;
        ctx.Response.ContentType = "application/json";
        object response;
        if (exception is null)
        {
            response = new
            {
                error = code,
                message = detail,
                exception = (object?)null
            };
        }
        else
        {
            response = new
            {
                error = code,
                message = detail,
                exception = (object?)new
                {
                    type = exception.GetType().FullName,
                    message = exception.Message,
                    stackTrace = exception.StackTrace,
                    details = exception.ToString()
                }
            };
        }

        await ctx.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
