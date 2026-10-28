using System.Globalization;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

internal static class ParsianStatus
{
    private static readonly Dictionary<short, string> Meanings = new()
    {
        [-112] = "The attempt OrderId is duplicated.",
        [-126] = "The merchant credential was rejected.",
        [-138] = "The customer cancelled payment.",
        [-1528] = "The original provider token was not found."
    };

    public static bool IsKnownRefusal(short code) => Meanings.ContainsKey(code);
    public static string Code(short code) => code.ToString(CultureInfo.InvariantCulture);

    public static Error Refusal(short code, string? message = null)
    {
        var description = Meanings.GetValueOrDefault(code, "The provider returned an unknown status; the payment outcome is unknown.");
        // Raw provider messages are returned for diagnostics, never logged by this adapter.
        return new("PARSIAN_STATUS_" + Code(code),
            string.IsNullOrEmpty(message) ? description : description + " " + message,
            code == -112 ? 409 : 502);
    }

    public static Error ReportFailure(short code, string? message = null)
    {
        var description = code switch
        {
            -8 => "The Parsian report request contains invalid input.",
            -99 => "The Parsian report service returned an operational failure.",
            _ => "The Parsian report service refused the request."
        };
        return new Error("PARSIAN_REPORT_STATUS_" + Code(code),
            string.IsNullOrWhiteSpace(message) ? description : description + " " + message,
            code == -8 ? 422 : 502);
    }
}
