namespace EwaysVNext.Modules.Partners.Adapter.Outbound.Usage;

/// <summary>
/// How usage logging trades memory, write amplification and loss against each other.
/// <para>
/// The capacity is the number of entries that may be waiting to be written. It is bounded on
/// purpose: an unbounded buffer turns a slow database into an out-of-memory crash, which is a
/// worse outcome than losing log lines.
/// </para>
/// </summary>
public sealed class PartnerUsageOptions
{
    public const string SectionName = "Partners:Usage";

    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>Entries per insert. One round trip per batch rather than per request.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>How long a partial batch waits before being written anyway.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Rows older than this are deleted by the UsagePrune job.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(90);
}
