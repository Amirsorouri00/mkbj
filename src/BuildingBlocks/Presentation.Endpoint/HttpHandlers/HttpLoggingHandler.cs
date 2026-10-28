using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Presentation.Endpoint.HttpHandlers;

public sealed class HttpLoggingHandler(ILogger<HttpLoggingHandler> logger, HttpLoggingOption httpLoggingOption) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            stopwatch.Stop();

            var responseBody = response.Content is null ? null : await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogInformation(
                HttpLoggingHandlerMessages.RequestCompleted,
                httpLoggingOption.ModuleName, httpLoggingOption.ServiceName, request.Method, request.RequestUri,
                (int)response.StatusCode, stopwatch.ElapsedMilliseconds, requestBody, responseBody);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                HttpLoggingHandlerMessages.RequestFailed,
                httpLoggingOption.ModuleName, httpLoggingOption.ServiceName, request.Method, request.RequestUri,
                stopwatch.ElapsedMilliseconds, requestBody);
            throw;
        }
    }
}
