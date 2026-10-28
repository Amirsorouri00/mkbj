using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using EwaysVNext.Modules.Partners.Application.Partners.Keys;
using Xunit;

namespace EwaysVNext.UnitTests.Partners;

/// <summary>
/// Keys are stored as HMAC-SHA256 under a server-side pepper, not as a slow password hash
/// (ADR-0021). A key is 256 bits of machine-generated entropy, so there is no dictionary to
/// attack and a work factor would buy nothing — it would only cost latency on every single
/// partner request. The pepper is what makes a stolen database table useless on its own.
/// </summary>
public sealed class ApiKeyHasherTests
{
    private static ApiKeyPepper Pepper(char fill = 'p') =>
        new(Encoding.UTF8.GetBytes(new string(fill, 32)));

    [Fact]
    public void Hash_Is_Hmac_Sha256_Of_Secret_Keyed_By_Pepper()
    {
        var pepper = Pepper();

        new ApiKeyHasher(pepper).Hash("s3cret")
            .Should().Equal(HMACSHA256.HashData(pepper.Value, "s3cret"u8.ToArray()));
    }

    [Fact]
    public void Matches_Accepts_The_Secret_It_Hashed()
    {
        var hasher = new ApiKeyHasher(Pepper());

        hasher.Matches("s3cret", hasher.Hash("s3cret")).Should().BeTrue();
    }

    [Fact]
    public void Matches_Rejects_A_Different_Secret()
    {
        var hasher = new ApiKeyHasher(Pepper());

        hasher.Matches("other", hasher.Hash("s3cret")).Should().BeFalse();
    }

    [Fact]
    public void Matches_Fails_Under_A_Different_Pepper()
    {
        var stored = new ApiKeyHasher(Pepper('a')).Hash("s3cret");

        new ApiKeyHasher(Pepper('b')).Matches("s3cret", stored).Should().BeFalse(
            "that is the whole point of a pepper: the table alone does not verify a key");
    }

    [Fact]
    public void Matches_Rejects_A_Stored_Hash_Of_The_Wrong_Length() =>
        new ApiKeyHasher(Pepper()).Matches("s3cret", [1, 2, 3]).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short-31-chars-0000000000000000")]
    public void Pepper_Shorter_Than_32_Chars_Is_Refused(string? raw) =>
        FluentActions.Invoking(() => ApiKeyPepper.FromConfiguration(raw))
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void A_32_Char_Pepper_Is_Accepted() =>
        ApiKeyPepper.FromConfiguration(new string('x', 32)).Value
            .Should().Equal(Encoding.UTF8.GetBytes(new string('x', 32)));

    [Fact]
    public void The_Refusal_Names_The_Setting() =>
        FluentActions.Invoking(() => ApiKeyPepper.FromConfiguration(null))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*ApiKeys:Pepper*", "an operator fixes a setting they can find");
}
