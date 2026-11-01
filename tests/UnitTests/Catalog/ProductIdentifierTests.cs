using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// Identifiers are how a product arriving from two feeds is recognised as one product, so a
/// value that is accepted in a shape nobody else writes it in is worse than one refused.
/// </summary>
public sealed class ProductIdentifierTests
{
    [Theory]
    [InlineData(ProductIdentifierType.Gtin, "4006381333931", "4006381333931")]
    [InlineData(ProductIdentifierType.Ean, "4006-3813 33931", "4006381333931")]
    [InlineData(ProductIdentifierType.Ean, "73513537", "73513537")]
    [InlineData(ProductIdentifierType.Upc, "036000291452", "036000291452")]
    [InlineData(ProductIdentifierType.Mpn, " ab-12/x ", "AB-12/X")]
    public void Normalize_Accepts_Valid(ProductIdentifierType type, string raw, string expected) =>
        ProductIdentifier.Normalize(type, raw, null).Should().Be(expected);

    [Theory]
    [InlineData(ProductIdentifierType.Gtin, "4006381333932", "IDENTIFIER_CHECK_DIGIT_INVALID")]
    [InlineData(ProductIdentifierType.Upc, "4006381333931", "IDENTIFIER_LENGTH_INVALID")]
    [InlineData(ProductIdentifierType.Ean, "40063813339A1", "IDENTIFIER_FORMAT_INVALID")]
    public void Normalize_Rejects_Invalid(ProductIdentifierType type, string raw, string code) =>
        FluentActions.Invoking(() => ProductIdentifier.Normalize(type, raw, null))
            .Should().Throw<DomainException>().Which.Code.Should().Be(code);

    [Theory]
    [InlineData("12345670")]          // GTIN-8
    [InlineData("036000291452")]      // GTIN-12
    [InlineData("4006381333931")]     // GTIN-13
    [InlineData("00012345600012")]    // GTIN-14
    public void Gtin_Accepts_Every_Standard_Length(string value) =>
        ProductIdentifier.Normalize(ProductIdentifierType.Gtin, value, null).Should().Be(value);

    [Fact]
    public void Gtin_Refuses_A_Length_That_Is_Not_One_Of_The_Four() =>
        FluentActions.Invoking(() => ProductIdentifier.Normalize(ProductIdentifierType.Gtin, "123456", null))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("IDENTIFIER_LENGTH_INVALID",
                "a six-digit code is a supplier's internal number, not a GTIN, and accepting it "
                + "would match two unrelated products the first time two feeds reuse the number");

    [Fact]
    public void Ean_Refuses_A_Twelve_Digit_Value() =>
        FluentActions.Invoking(() => ProductIdentifier.Normalize(ProductIdentifierType.Ean, "036000291452", null))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("IDENTIFIER_LENGTH_INVALID", "EAN is 8 or 13; 12 is a UPC");

    [Fact]
    public void An_Empty_Value_Is_Refused() =>
        FluentActions.Invoking(() => ProductIdentifier.Normalize(ProductIdentifierType.Mpn, "   ", null))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("IDENTIFIER_VALUE_REQUIRED");

    [Fact]
    public void Mpn_Refuses_More_Than_Sixty_Four_Characters() =>
        FluentActions.Invoking(() => ProductIdentifier.Normalize(
                ProductIdentifierType.Mpn, new string('A', 65), null))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("IDENTIFIER_LENGTH_INVALID");

    [Fact]
    public void Mpn_Keeps_Its_Punctuation()
    {
        // Unlike a GTIN, dashes and slashes are part of a manufacturer part number.
        ProductIdentifier.Normalize(ProductIdentifierType.Mpn, "mx-900/b", null).Should().Be("MX-900/B");
    }

    [Fact]
    public void SourceId_Requires_Domain() =>
        FluentActions.Invoking(() => ProductIdentifier.Normalize(ProductIdentifierType.SourceId, "123", null))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("IDENTIFIER_SOURCE_DOMAIN_REQUIRED");

    [Fact]
    public void SourceId_Keeps_Its_Value_Verbatim_But_Trimmed() =>
        ProductIdentifier.Normalize(ProductIdentifierType.SourceId, "  SKU_aB-9  ", "example.com")
            .Should().Be("SKU_aB-9",
                "the value is the other system's key, so changing its case would stop it matching");

    [Fact]
    public void A_Domain_On_A_Type_That_Is_Not_SourceId_Is_Refused() =>
        FluentActions.Invoking(() => ProductIdentifier.Normalize(
                ProductIdentifierType.Gtin, "4006381333931", "example.com"))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("IDENTIFIER_SOURCE_DOMAIN_NOT_ALLOWED",
                "a GTIN means the same thing wherever it came from");

    [Theory]
    [InlineData("4006381333931", true)]
    [InlineData("4006381333932", false)]
    [InlineData("73513537", true)]
    [InlineData("036000291452", true)]
    [InlineData("00012345600012", true)]
    public void Check_Digits_Are_Mod_Ten_With_Alternating_Weights(string digits, bool valid) =>
        Gs1.HasValidCheckDigit(digits).Should().Be(valid);

    [Fact]
    public void Create_Normalizes_Through_The_Same_Path()
    {
        var identifier = ProductIdentifier.Create(
            1, 2, ProductIdentifierType.Ean, "4006-3813 33931", null);

        identifier.Value.Should().Be("4006381333931");
        identifier.ProductId.Should().Be(2);
        identifier.Type.Should().Be(ProductIdentifierType.Ean);
        identifier.SourceDomain.Should().BeNull();
    }

    [Fact]
    public void Create_Lower_Cases_The_Source_Domain()
    {
        var identifier = ProductIdentifier.Create(
            1, 2, ProductIdentifierType.SourceId, "AB12", "  Example.COM  ");

        identifier.SourceDomain.Should().Be("example.com",
            "a host is case-insensitive, so storing two casings would make one source look like two");
    }
}
