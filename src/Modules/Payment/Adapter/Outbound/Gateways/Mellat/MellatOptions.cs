namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat;

public sealed class MellatOptions
{
    public const string SectionName = "Payment:Gateways:Mellat";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://bpm.shaparak.ir/pgwchannel/rest";
    public string PaymentPageUrl { get; set; } = "https://bpm.shaparak.ir/pgwchannel/startpay.mellat";
    public long TerminalId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserPassword { get; set; } = string.Empty;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    internal static bool IsHttpsAddress(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
