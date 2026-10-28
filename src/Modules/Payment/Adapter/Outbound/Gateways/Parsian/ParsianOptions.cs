namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

public sealed class ParsianOptions
{
    public const string SectionName = "Payment:Gateways:Parsian";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://pec.shaparak.ir/NewIPGRestServices";
    public string PaymentPageUrl { get; set; } = "https://pec.shaparak.ir/NewIPG/";
    public string LoginAccount { get; set; } = string.Empty;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    internal static bool IsHttpsAddress(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
