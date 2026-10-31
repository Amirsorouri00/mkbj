using System.Threading.Channels;

using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
using EwaysVNext.Modules.Partners.Application.Usage;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Modules.Partners.Adapter.Outbound.Usage;

/// <summary>
/// A bounded in-memory queue between the request thread and the writer.
/// <para>
/// Bounded on purpose: an unbounded buffer turns a slow database into an out-of-memory crash,
/// which is worse than losing log lines. Under sustained overload this sheds entries and keeps
/// serving.
/// </para>
/// <para>
/// <c>FullMode.Wait</c> looks like the wrong choice here and is not. It governs writers that
/// <i>await</i>; this one only ever calls <see cref="ChannelWriter{T}.TryWrite"/>, which never
/// blocks under any mode and returns <c>false</c> when the channel is full. <c>DropWrite</c>
/// would make <c>TryWrite</c> return <c>true</c> while silently discarding the entry, leaving no
/// way to count what was lost — and a drop nobody can see is indistinguishable from a bug in the
/// writer.
/// </para>
/// <para>
/// In-process, so entries still buffered at shutdown are lost — see ADR-0010, which must be
/// settled before running more than one replica.
/// </para>
/// </summary>
public sealed class ChannelApiKeyUsageRecorder : IApiKeyUsageRecorder
{
    private readonly Channel<ApiKeyUsageEntry> _channel;

    public ChannelApiKeyUsageRecorder(IOptions<PartnerUsageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _channel = Channel.CreateBounded<ApiKeyUsageEntry>(
            new BoundedChannelOptions(options.Value.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
            });
    }

    public ChannelReader<ApiKeyUsageEntry> Reader => _channel.Reader;

    public bool TryRecord(ApiKeyUsageEntry entry) => _channel.Writer.TryWrite(entry);
}
