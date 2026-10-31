using AwesomeAssertions;
using EwaysVNext.Modules.Partners.Adapter.Outbound.Usage;
using EwaysVNext.Modules.Partners.Application.Usage;
using Microsoft.Extensions.Options;
using Xunit;

namespace EwaysVNext.UnitTests.Partners;

/// <summary>
/// The recorder's only hard requirement is that it never makes a partner wait. Everything else —
/// how many entries it holds, how they are batched — is tuning.
/// </summary>
public sealed class ChannelApiKeyUsageRecorderTests
{
    private static ApiKeyUsageEntry Entry() =>
        new(1, 10, TestTime.Now, "GET", "/test", 200, "corr", 5);

    private static ChannelApiKeyUsageRecorder Recorder(int capacity) =>
        new(Options.Create(new PartnerUsageOptions { ChannelCapacity = capacity }));

    [Fact]
    public void TryRecord_Returns_False_Instead_Of_Blocking_When_Full()
    {
        var recorder = Recorder(capacity: 2);

        recorder.TryRecord(Entry()).Should().BeTrue();
        recorder.TryRecord(Entry()).Should().BeTrue();

        recorder.TryRecord(Entry()).Should().BeFalse(
            "a full buffer drops the line rather than stalling the request that produced it");
    }

    [Fact]
    public async Task A_Dropped_Entry_Does_Not_Poison_The_Buffer()
    {
        var recorder = Recorder(capacity: 1);

        recorder.TryRecord(Entry()).Should().BeTrue();
        recorder.TryRecord(Entry()).Should().BeFalse();

        // Draining makes room again; the recorder is not left in a failed state.
        (await recorder.Reader.ReadAsync()).Should().NotBeNull();
        recorder.TryRecord(Entry()).Should().BeTrue();
    }

    [Fact]
    public async Task Entries_Come_Back_In_The_Order_They_Were_Recorded()
    {
        var recorder = Recorder(capacity: 10);

        recorder.TryRecord(Entry() with { Path = "/first" });
        recorder.TryRecord(Entry() with { Path = "/second" });

        (await recorder.Reader.ReadAsync()).Path.Should().Be("/first");
        (await recorder.Reader.ReadAsync()).Path.Should().Be("/second");
    }
}
