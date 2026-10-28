namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;

public sealed class ParsianInquiryOptions
{
    public const string SectionName = ParsianOptions.SectionName + ":Inquiry";

    public string BaseUrl { get; set; } = "https://pgwservices.pec.ir";
    public string ApiVersion { get; set; } = "1.0";
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
