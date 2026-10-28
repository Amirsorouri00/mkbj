using System.Globalization;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat.Contracts;

internal sealed record MellatCallbackFormPayload(
    string RefId, string ResCode, long SaleOrderId, long? SaleReferenceId)
{
    public static MellatCallbackFormPayload? Parse(IReadOnlyDictionary<string, string> source)
    {
        var fields = new Dictionary<string, string>(source, StringComparer.Ordinal);
        if (!fields.TryGetValue("RefId", out var refId) || string.IsNullOrEmpty(refId)
            || !fields.TryGetValue("ResCode", out var resCode) || string.IsNullOrWhiteSpace(resCode)
            || !fields.TryGetValue("SaleOrderId", out var rawOrderId)
            || !long.TryParse(rawOrderId, NumberStyles.None, CultureInfo.InvariantCulture, out var saleOrderId)
            || saleOrderId <= 0)
            return null;

        long? saleReferenceId = null;
        if (fields.TryGetValue("SaleReferenceId", out var rawReference) && !string.IsNullOrWhiteSpace(rawReference))
        {
            if (!long.TryParse(rawReference, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedReference)
                || parsedReference <= 0)
                return null;
            saleReferenceId = parsedReference;
        }

        return new(refId, resCode, saleOrderId, saleReferenceId);
    }
}
