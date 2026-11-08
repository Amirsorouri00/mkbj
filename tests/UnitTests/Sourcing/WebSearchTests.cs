using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Modules.Sourcing.Adapter.Outbound.Search;
using Xunit;

namespace EwaysVNext.UnitTests.Sourcing;

/// <summary>
/// Reading a search engine's answer.
/// </summary>
/// <remarks>
/// <b>None of this touches the live service.</b> It pins the mapping against a recorded payload
/// shape, which catches a rename on this side and nothing on theirs. The first live run is the
/// real test, and the phase report says so.
/// </remarks>
public sealed class WebSearchTests
{
    [Fact]
    public void A_Response_Becomes_Urls_To_Look_At()
    {
        const string Payload = """
            {"web":{"results":[
              {"url":"https://shop.example.com/p/1","title":"Steel Kettle"},
              {"url":"https://other.example.com/p/2","title":"Glass Kettle"}]}}
            """;

        var hits = BraveSearchProvider.Map(
            JsonSerializer.Deserialize<BraveSearchProvider.BraveResponse>(Payload));

        hits.Select(hit => hit.Url).Should().Equal(
            "https://shop.example.com/p/1", "https://other.example.com/p/2");

        // Titles come back, but only to show an operator what was found. The product data comes
        // from reading the page, never from the snippet.
        hits[0].Title.Should().Be("Steel Kettle");
    }

    [Fact]
    public void A_Result_With_No_Url_Is_Not_A_Page_To_Read()
    {
        const string Payload = """{"web":{"results":[{"title":"No link here"}]}}""";

        BraveSearchProvider.Map(
            JsonSerializer.Deserialize<BraveSearchProvider.BraveResponse>(Payload))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"web":null}""")]
    [InlineData("""{"web":{"results":null}}""")]
    public void An_Answer_With_Nothing_In_It_Is_Not_An_Error(string payload) =>
        // A search that found nothing is an ordinary outcome, and the crawl sources in the same
        // job are still worth reading.
        BraveSearchProvider.Map(
            JsonSerializer.Deserialize<BraveSearchProvider.BraveResponse>(payload))
            .Should().BeEmpty();
}
