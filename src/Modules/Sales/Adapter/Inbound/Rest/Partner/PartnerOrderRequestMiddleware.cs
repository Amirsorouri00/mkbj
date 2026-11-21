using System.Text;
using System.Text.Json;
using Mkbj.Core.Application;
using Mkbj.Domain.Common;
using Mkbj.Modules.Sales.Application.Domain.Telemetry;
using Mkbj.Modules.Sales.Application.Ports.Outbound;
using Mkbj.Outcomes;
using Mkbj.Presentation.Endpoint;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Mkbj.Modules.Sales.Adapter.Inbound.Rest.Partner;

/// <summary>Persists input before executing and output before sending an order response.</summary>
public sealed class PartnerOrderRequestMiddleware(RequestDelegate next)
{
    internal static readonly object ReceivedAtKey = new();
    private const int MaxBodyBytes = 1_048_576;

    public async Task InvokeAsync(HttpContext context, IPartnerOrderRequestJournal journal,
        IIdGenerator ids, IClock clock, ILogger<PartnerOrderRequestMiddleware> logger)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
        if (!HttpMethods.IsPost(context.Request.Method)
            || path is not ("/api/v1/orders" or "/api/v1/orders/bulk")
            || !PartnerPrincipal.TryFrom(context.User, out var principal))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        using var input = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
        {
            if (input.Length + read > MaxBodyBytes)
            {
                await EndpointResult.Fail(new Error("REQUEST_TOO_LARGE",
                    "Order submissions must be at most 1 MiB.", 413)).ExecuteAsync(context);
                return;
            }
            await input.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
        }
        context.Request.Body.Position = 0;
        var body = Encoding.UTF8.GetString(input.ToArray());
        var receipt = new PartnerOrderRequest
        {
            Id = ids.Generate(), PartnerId = principal!.PartnerId, KeyId = principal.ApiKeyId,
            ReceivedAt = clock.UtcNow, CorrelationId = context.TraceIdentifier, Path = path,
            RequestBody = body, ExternalOrderIds = ExternalIds(body),
        };
        await journal.ReceiveAsync(receipt, context.RequestAborted);
        context.Items[ReceivedAtKey] = receipt.ReceivedAt;
        context.Response.Headers["X-Request-Id"] = receipt.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var destination = context.Response.Body;
        await using var output = new MemoryStream();
        context.Response.Body = output;
        try
        {
            try
            {
                await next(context);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Partner order request {RequestId} interrupted; retry with the same external_order_id.", receipt.Id);
                output.SetLength(0);
                context.Response.ContentLength = null;
                // The order may already have committed. Retrying the same external id resolves it.
                await EndpointResult.Fail(new Error("ORDER_REQUEST_INTERRUPTED",
                    "The result could not be confirmed. Retry with the same external_order_id.",
                    503, Retryable: true)).ExecuteAsync(context);
            }

            // A disconnected client must not cancel recording the outcome of a committed order.
            using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await journal.CompleteAsync(receipt.Id, clock.UtcNow, context.Response.StatusCode,
                Encoding.UTF8.GetString(output.ToArray()),
                context.Response.Headers["Idempotent-Replay"] == "true", completion.Token);
            output.Position = 0;
            context.Response.Body = destination;
            await output.CopyToAsync(destination, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = destination;
        }
    }

    private static string[] ExternalIds(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return [];
            var items = root.TryGetProperty("items", out var batch) && batch.ValueKind == JsonValueKind.Array
                ? batch.EnumerateArray().ToArray() : [root];
            return items.Where(item => item.ValueKind == JsonValueKind.Object)
                .Where(item => item.TryGetProperty("external_order_id", out var id) && id.ValueKind == JsonValueKind.String)
                .Select(item => item.GetProperty("external_order_id").GetString()!)
                .Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (JsonException) { return []; }
    }
}
