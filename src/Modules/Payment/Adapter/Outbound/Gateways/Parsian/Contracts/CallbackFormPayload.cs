using System.Globalization;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;

/// <summary>The driving adapter supplies decoded form fields, not a JSON callback body.</summary>
internal sealed record CallbackFormPayload(short Status, string RawStatus, long? Rrn, string? RawRrn)
{
    public static CallbackFormPayload? Parse(IReadOnlyDictionary<string, string> fields)
    {
        // Do not inherit a caller's case-insensitive dictionary comparer.
        var form = new Dictionary<string, string>(fields, StringComparer.Ordinal);
        if (!form.TryGetValue("status", out var rawStatus)
            || !short.TryParse(rawStatus, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var status))
            return null;

        form.TryGetValue("RRN", out var rawRrn);
        long? rrn = long.TryParse(rawRrn, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : null;
        return new(status, rawStatus, rrn, rrn.HasValue ? rawRrn : null);
    }
}
