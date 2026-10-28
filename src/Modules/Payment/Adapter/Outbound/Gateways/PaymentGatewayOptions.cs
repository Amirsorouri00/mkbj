namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;

public sealed class PaymentGatewayOptions
{
    public const string SectionName = "Payment:Gateways";
    public string? DefaultProvider { get; set; }
    public List<string> FallbackProviders { get; set; } = [];
    public TimeSpan LinkCreationTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
