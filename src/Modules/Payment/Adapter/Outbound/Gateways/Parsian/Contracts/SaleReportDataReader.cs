using System.Text.Json;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;

internal static class SaleReportDataReader
{
    public static Result<SaleReportResponse> Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Status", out var statusElement)
                || !statusElement.TryGetInt16(out var status))
            {
                return Invalid();
            }

            string? message = null;
            if (root.TryGetProperty("Message", out var messageElement))
            {
                if (messageElement.ValueKind == JsonValueKind.String)
                    message = messageElement.GetString();
                else if (messageElement.ValueKind != JsonValueKind.Null)
                    return Invalid();
            }

            if (status != 0)
                return Result.Success(new SaleReportResponse(status, message, []));
            if (!root.TryGetProperty("Data", out var data))
                return Invalid();

            var records = new List<SaleReportRecord>();
            if (data.ValueKind == JsonValueKind.Object)
            {
                var record = ReadRecord(data);
                if (record is null) return Invalid();
                records.Add(record);
            }
            else if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in data.EnumerateArray())
                {
                    var record = ReadRecord(element);
                    if (record is null) return Invalid();
                    records.Add(record);
                }
            }
            else
            {
                return Invalid();
            }

            return Result.Success(new SaleReportResponse(status, message, records));
        }
        catch (JsonException)
        {
            return Invalid();
        }
    }

    private static SaleReportRecord? ReadRecord(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !TryInt64(element, "Amount", out var amount)
            || !TryInt64(element, "OrderId", out var orderId)
            || !TryInt64(element, "Token", out var token)
            || !TryInt64(element, "RRN", out var rrn))
        {
            return null;
        }

        DateTime? requestedAt = null;
        if (element.TryGetProperty("RequestDateTime", out var dateElement))
        {
            if (dateElement.ValueKind != JsonValueKind.String || !dateElement.TryGetDateTime(out var parsed))
                return null;
            requestedAt = parsed;
        }

        short? pgwStatus = null;
        if (element.TryGetProperty("PGWStatusId", out var statusElement))
        {
            if (!statusElement.TryGetInt16(out var parsed))
                return null;
            pgwStatus = parsed;
        }

        return new SaleReportRecord(amount, orderId, token, rrn, requestedAt, pgwStatus);
    }

    private static bool TryInt64(JsonElement element, string name, out long value)
    {
        value = default;
        return element.TryGetProperty(name, out var property) && property.TryGetInt64(out value);
    }

    private static Result<SaleReportResponse> Invalid() =>
        Result.Failure<SaleReportResponse>(PaymentGatewayErrors.InvalidResponse);
}
