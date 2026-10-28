using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace EwaysVNext.Modules.Customer.Application.Service.Http;


public sealed class ShahkarLoggingHandler(ILogger<ShahkarLoggingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation(
            ShahkarLoggingHandlerMessages.RequestStarted,
            request.Method,
            request.RequestUri);

        try
        {
            var response = await base.SendAsync(
                request,
                cancellationToken);

            stopwatch.Stop();

            logger.LogInformation(
                ShahkarLoggingHandlerMessages.RequestCompleted,
                request.Method,
                request.RequestUri,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            logger.LogWarning(
                ShahkarLoggingHandlerMessages.RequestCancelled,
                request.Method,
                request.RequestUri,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            logger.LogError(
                ex,
                ShahkarLoggingHandlerMessages.RequestFailed,
                request.Method,
                request.RequestUri,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
}

