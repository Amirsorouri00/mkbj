using AwesomeAssertions;
using EwaysVNext.Modules.Partners.Application.Partners.Keys;
using Xunit;

namespace EwaysVNext.UnitTests.Partners;

/// <summary>
/// The key format is a wire contract (ADR-0021): <c>pk_&lt;8 prefix&gt;_&lt;43 secret&gt;</c>.
/// <para>
/// The prefix exists so a presented key can be found in the database with an indexed lookup
/// instead of hashing every stored row, and the <c>pk_</c> marker so the authentication pipeline
/// can route a partner key away from the JWT reader without a database call at all. Both make the
/// lengths load-bearing rather than cosmetic.
/// </para>
/// </summary>
public sealed class ApiKeyFormatTests
{
    [Fact]
    public void Generate_Produces_The_Documented_Format()
    {
        var key = ApiKeyFormat.Generate();

        key.Plaintext.Should().MatchRegex("^pk_[0-9A-Za-z]{8}_[0-9A-Za-z]{43}$");
        key.Plaintext.Should().Be($"pk_{key.Prefix}_{key.Secret}");
        ApiKeyFormat.Generate().Secret.Should().NotBe(key.Secret);
    }

    [Fact]
    public void TryParse_Round_Trips()
    {
        var key = ApiKeyFormat.Generate();

        ApiKeyFormat.TryParse(key.Plaintext, out var prefix, out var secret).Should().BeTrue();
        prefix.Should().Be(key.Prefix);
        secret.Should().Be(key.Secret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pk_")]
    [InlineData("PK_ABCDEFGH_0000000000000000000000000000000000000000000")]
    [InlineData("pk_ABCDEFG_0000000000000000000000000000000000000000000")]
    [InlineData("pk_ABCDEFGH_000000000000000000000000000000000000000000")]
    [InlineData("pk_ABCDEFGH_00000000000000000000000000000000000000000000")]
    [InlineData("pk_ABCDEFGH_000000000000000000000000000000000000000000-")]
    [InlineData("pk_ABC_EFGH_0000000000000000000000000000000000000000000")]
    public void TryParse_Rejects_Malformed(string presented) =>
        ApiKeyFormat.TryParse(presented, out _, out _).Should().BeFalse();

    [Fact]
    public void TryParse_Rejects_Null() =>
        ApiKeyFormat.TryParse(null!, out _, out _).Should().BeFalse();

    [Fact]
    public void Generated_Prefixes_Are_Not_All_The_Same() =>
        Enumerable.Range(0, 20).Select(_ => ApiKeyFormat.Generate().Prefix)
            .Distinct(StringComparer.Ordinal).Should().HaveCountGreaterThan(1,
                "a constant prefix would make every lookup scan the whole table");

    [Fact]
    public void The_Secret_Is_Long_Enough_To_Carry_256_Bits()
    {
        // 62^43 > 2^256, so the encoding never truncates the 32 random bytes it came from. One
        // character fewer and it would: 62^42 < 2^256.
        var space = System.Numerics.BigInteger.Pow(62, ApiKeyFormat.SecretLength);

        space.Should().BeGreaterThan(System.Numerics.BigInteger.Pow(2, 256));
        System.Numerics.BigInteger.Pow(62, ApiKeyFormat.SecretLength - 1)
            .Should().BeLessThan(System.Numerics.BigInteger.Pow(2, 256));
    }
}
