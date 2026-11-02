using AwesomeAssertions;
using EwaysVNext.Modules.Catalog.QueryApplication.Storefront;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// The paging cursor, which arrives from the internet and must never throw on the way in.
/// </summary>
public sealed class StorefrontCursorTests
{
    [Theory]
    [InlineData(StorefrontSort.PriceAsc, "2500")]
    [InlineData(StorefrontSort.Newest, "2026-11-02T10:00:00.1234560+00:00")]
    [InlineData(StorefrontSort.Relevance, "0.0607927")]
    public void Cursor_Roundtrips(StorefrontSort sort, string key)
    {
        var original = new StorefrontCursor(StorefrontCursor.FtsMode, sort, key, 1234567890123L);

        StorefrontCursor.TryDecode(original.Encode(), out var decoded).Should().BeTrue();

        decoded.Should().Be(original);
    }

    [Fact]
    public void Encoding_Is_Url_Safe()
    {
        var encoded = new StorefrontCursor(
            StorefrontCursor.TrigramMode, StorefrontSort.PriceDesc, "999999", long.MaxValue).Encode();

        // base64url, so it survives a query string without escaping — a cursor that needed
        // percent-encoding would come back mangled by at least one client.
        encoded.Should().NotContainAny("+", "/", "=");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64-at-all!!")]
    [InlineData("eyJhIjoxfQ")]                     // valid base64url, valid JSON, wrong shape
    [InlineData("eyJtb2RlIjoiaGFjayJ9")]           // {"mode":"hack"} — a mode this shop never issues
    public void Garbage_Cursor_Does_Not_Decode(string? text)
    {
        StorefrontCursor.TryDecode(text, out var cursor).Should().BeFalse();

        cursor.Should().BeNull("a caller that ignores the bool must not get a usable key");
    }

    [Fact]
    public void An_Over_Long_Cursor_Is_Refused_Without_Decoding()
    {
        // Nothing this shop issues is near this long, so the only sender is someone probing.
        StorefrontCursor.TryDecode(new string('A', 10_000), out _).Should().BeFalse();
    }

    [Fact]
    public void An_Undefined_Sort_Does_Not_Decode()
    {
        // {"mode":"fts","sort":99,"key":"1","productId":1}
        const string Text = "eyJtb2RlIjoiZnRzIiwic29ydCI6OTksImtleSI6IjEiLCJwcm9kdWN0SWQiOjF9";

        StorefrontCursor.TryDecode(Text, out _).Should().BeFalse(
            "an out-of-range enum would reach the SQL builder as a sort key that does not exist");
    }
}
