namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat;

public sealed class MellatInquiryOptions
{
    public const string SectionName = MellatOptions.SectionName + ":Inquiry";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://bos.bpm.bankmellat.ir";
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
