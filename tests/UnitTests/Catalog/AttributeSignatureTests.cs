using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// A variant's attribute signature is what decides whether two rows describe the same variant.
/// Phase 7's sourcing module computes it independently from a crawled listing, so every detail
/// here — the sort, the escaping, the whitespace collapse — is a wire format, not an
/// implementation choice.
/// </summary>
public sealed class AttributeSignatureTests
{
    private static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void From_Sorts_Canonical_Keys_And_Lowercases_Values()
    {
        var signature = AttributeSignature.From(new Dictionary<string, string>
        {
            ["Size"] = " XL ",
            ["Colour Name"] = "Deep  Red"
        });

        signature.Text.Should().Be("colour-name=deep red;size=xl");
        signature.Hash.Should().Be(Sha256Hex("colour-name=deep red;size=xl"));
        signature.Values.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["colour-name"] = "Deep Red",
            ["size"] = "XL"
        });
    }

    [Fact]
    public void Empty_Has_Sha256_Of_Empty_String() =>
        AttributeSignature.From(new Dictionary<string, string>()).Hash
            .Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

    [Fact]
    public void The_Empty_Signature_Is_The_Same_As_One_From_No_Attributes()
    {
        AttributeSignature.Empty.Hash.Should().Be(
            AttributeSignature.From(new Dictionary<string, string>()).Hash);
        AttributeSignature.Empty.Text.Should().BeEmpty();
        AttributeSignature.Empty.Values.Should().BeEmpty();
    }

    [Fact]
    public void Separators_In_Values_Are_Escaped() =>
        AttributeSignature.From(new Dictionary<string, string> { ["a"] = "x=y;z%" })
            .Text.Should().Be("a=x%3dy%3bz%25");

    [Fact]
    public void Escaping_Is_Unambiguous_Because_Percent_Is_Escaped_First()
    {
        // "%3b" typed by a human must not come back indistinguishable from a real ';'.
        var literal = AttributeSignature.From(new Dictionary<string, string> { ["a"] = "%3b" }).Text;
        var separator = AttributeSignature.From(new Dictionary<string, string> { ["a"] = ";" }).Text;

        literal.Should().Be("a=%253b");
        separator.Should().Be("a=%3b");
        literal.Should().NotBe(separator);
    }

    [Fact]
    public void Keys_Colliding_After_Canonicalization_Are_Rejected() =>
        FluentActions.Invoking(() => AttributeSignature.From(new Dictionary<string, string>
                { ["Size"] = "M", ["size"] = "L" }))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("VARIANT_ATTRIBUTE_DUPLICATE_KEY",
                "one of the two values would silently win, and which one would depend on "
                + "dictionary order");

    [Fact]
    public void A_Blank_Value_Is_Rejected() =>
        FluentActions.Invoking(() => AttributeSignature.From(new Dictionary<string, string>
                { ["Size"] = "   " }))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("VARIANT_ATTRIBUTE_VALUE_EMPTY");

    [Fact]
    public void A_Key_That_Canonicalises_To_Nothing_Is_Rejected() =>
        FluentActions.Invoking(() => AttributeSignature.From(new Dictionary<string, string>
                { [" -- "] = "M" }))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("CANONICAL_KEY_EMPTY");

    [Fact]
    public void The_Sort_Is_Ordinal_And_Applied_To_The_Canonical_Key()
    {
        // "Z Thing" sorts before "a thing" by ordinal on the raw keys, and after it on the
        // canonical ones. The canonical order is the one that has to hold.
        var signature = AttributeSignature.From(new Dictionary<string, string>
        {
            ["Z Thing"] = "1",
            ["a thing"] = "2"
        });

        signature.Text.Should().Be("a-thing=2;z-thing=1");
    }

    [Fact]
    public void Insertion_Order_Does_Not_Change_The_Hash()
    {
        var first = AttributeSignature.From(new Dictionary<string, string>
            { ["Size"] = "XL", ["Colour"] = "Red" });
        var second = AttributeSignature.From(new Dictionary<string, string>
            { ["Colour"] = "Red", ["Size"] = "XL" });

        second.Hash.Should().Be(first.Hash,
            "the sourcing module builds its dictionary from a crawled page in whatever order the "
            + "page lists the attributes");
    }

    [Fact]
    public void Values_Differing_Only_In_Case_Or_Spacing_Produce_One_Hash()
    {
        var first = AttributeSignature.From(new Dictionary<string, string> { ["Colour"] = "Deep Red" });
        var second = AttributeSignature.From(new Dictionary<string, string> { ["colour"] = " deep   RED " });

        second.Hash.Should().Be(first.Hash);
        second.Values["colour"].Should().Be("deep RED",
            "the display value keeps the supplier's capitalisation even though the hash ignores it");
    }

    [Fact]
    public void HashOf_Is_The_Shorthand_For_From_Dot_Hash()
    {
        // Named HashOf rather than Hash: a static method cannot share a name with the record's
        // own Hash property. Phase 7 calls this to compute the identical hash from a listing.
        var attributes = new Dictionary<string, string> { ["Size"] = "XL" };

        AttributeSignature.HashOf(attributes).Should().Be(AttributeSignature.From(attributes).Hash);
    }

    [Fact]
    public void A_Null_Dictionary_Is_Refused() =>
        FluentActions.Invoking(() => AttributeSignature.From(null!))
            .Should().Throw<ArgumentNullException>();
}
