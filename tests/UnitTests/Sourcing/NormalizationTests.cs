using System.Text.Json.Nodes;
using AwesomeAssertions;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Normalization;
using EwaysVNext.Modules.Sourcing.Application.Validation;
using Xunit;

namespace EwaysVNext.UnitTests.Sourcing;

/// <summary>
/// Turning what a source wrote into what this system means.
/// </summary>
/// <remarks>
/// Deterministic throughout: the same raw document always produces the same normalized product,
/// so a re-import changes nothing unless the source changed, and a reviewer months later sees
/// what the pipeline saw.
/// </remarks>
public sealed class NormalizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("19.99", 1_999)]
    [InlineData("$1,234.56", 123_456)]
    [InlineData("1.234,56 €", 123_456)]
    [InlineData("1 234,56", 123_456)]
    [InlineData("1'234.56", 123_456)]
    [InlineData("19", 1_900)]
    [InlineData("0.5", 50)]
    public void A_Price_Is_Read_Whichever_Way_The_Source_Writes_It(string text, long expected) =>
        PriceParser.ToMinor(text).Should().Be(expected);

    [Fact]
    public void Three_Digits_After_The_Last_Separator_Mean_It_Was_Grouping() =>
        // The case a naive decimal parse gets wrong: 1.234 is one thousand two hundred and
        // thirty-four, not one pound twenty-three.
        PriceParser.ToMinor("1.234").Should().Be(123_400);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("call for price")]
    public void Something_That_Is_Not_A_Price_Reads_As_Nothing(string? text) =>
        // Null rather than zero: zero is a price somebody might charge.
        PriceParser.ToMinor(text).Should().BeNull();

    [Theory]
    [InlineData("4006381333931")]
    [InlineData("0 4006381 33393 1")]
    [InlineData("96385074")]
    public void A_Real_Barcode_Passes_Its_Check_Digit(string text) =>
        GtinValidator.Normalize(text).Should().NotBeNull();

    [Theory]
    [InlineData("4006381333932")]
    [InlineData("1234567890123")]
    [InlineData("12345")]
    public void A_Mistyped_Barcode_Is_Refused(string text) =>
        // A wrong barcode attaches a listing to somebody else's product, which is the expensive
        // mistake this module is arranged to avoid.
        GtinValidator.Normalize(text).Should().BeNull();

    [Fact]
    public void Normalizing_Records_Where_Every_Field_Came_From()
    {
        var product = DeterministicNormalizer.Normalize(Listing(new JsonObject
        {
            ["name"] = "  Steel   Kettle\n1.7L ",
            ["brand"] = new JsonObject { ["name"] = "Acme" },
            ["gtin13"] = "4006381333931",
            ["offers"] = new JsonObject { ["price"] = "$19.99", ["priceCurrency"] = "sgd" },
        }));

        product.Name.Should().Be("Steel Kettle 1.7L",
            "scraped text carries the page's own layout, which is not part of the name");
        product.Brand.Should().Be("Acme");
        product.Gtin.Should().Be("4006381333931");
        product.PriceMinor.Should().Be(1_999);
        product.Currency.Should().Be("SGD");

        // A published product nobody can trace back to a line in a supplier's file is one
        // nobody can defend when it is disputed.
        product.Provenance[nameof(NormalizedProduct.Brand)].Should().Be("/brand/name");
        product.Provenance[nameof(NormalizedProduct.PriceMinor)].Should().Be("/offers/price");
    }

    [Fact]
    public void An_Invalid_Barcode_Is_Dropped_Rather_Than_Carried()
    {
        var product = DeterministicNormalizer.Normalize(
            Listing(new JsonObject { ["name"] = "Kettle", ["gtin13"] = "4006381333932" }));

        product.Gtin.Should().BeNull();

        // The validator says the source had one and it did not add up — otherwise the record
        // would look as though the source never provided a barcode at all.
        RecordValidator.IdentifierIssue("4006381333932", product.Gtin)
            .Should().Be(new ValidationIssue(
                ValidationCodes.IdentifierInvalid, IssueSeverity.Warning, "4006381333932"));
    }

    [Fact]
    public void Nothing_Is_Invented()
    {
        var product = DeterministicNormalizer.Normalize(
            Listing(new JsonObject { ["name"] = "Mystery Kettle" }));

        // Guessing a brand from a title is how a catalogue fills with brands that do not exist.
        product.Brand.Should().BeNull();
        product.PriceMinor.Should().BeNull();
        product.Currency.Should().BeNull();
    }

    [Fact]
    public void The_Variant_Key_Does_Not_Depend_On_Attribute_Order()
    {
        var one = DeterministicNormalizer.Normalize(Listing(new JsonObject
        {
            ["name"] = "Kettle",
            ["additionalProperty"] = new JsonObject { ["colour"] = "Red", ["size"] = "L" },
        }));

        var other = DeterministicNormalizer.Normalize(Listing(new JsonObject
        {
            ["name"] = "Kettle",
            ["additionalProperty"] = new JsonObject { ["size"] = "L", ["colour"] = "Red" },
        }));

        one.VariantKey().Should().Be(other.VariantKey());
    }

    [Fact]
    public void Different_Variants_Have_Different_Keys()
    {
        var red = Variant("Red");
        var blue = Variant("Blue");

        red.VariantKey().Should().NotBe(blue.VariantKey());
    }

    [Fact]
    public void A_Record_With_Nothing_To_Publish_Is_Invalid()
    {
        var issues = RecordValidator.Validate(
            DeterministicNormalizer.Normalize(Listing(new JsonObject())),
            new RightsDecision(RightsStatus.Allowed, "policy:x"),
            requireCategory: false);

        issues.Should().Contain(issue =>
            issue.Code == ValidationCodes.TitleMissing && issue.Severity == IssueSeverity.Error);

        StagedRecord.DeriveStatus(MatchResult.New, issues)
            .Should().Be(StagedRecordStatus.Invalid);
    }

    [Fact]
    public void Denied_Content_Is_An_Error_Because_Nobody_Can_Grant_It()
    {
        var issues = RecordValidator.Validate(
            Complete(),
            new RightsDecision(RightsStatus.ContentDenied, "policy:x"),
            requireCategory: false);

        // No one in the backoffice can wave through a right the supplier withheld.
        issues.Should().Contain(issue =>
            issue.Code == ValidationCodes.RightsContentDenied
            && issue.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Unknown_Rights_Ask_A_Person()
    {
        var issues = RecordValidator.Validate(
            Complete(), new RightsDecision(RightsStatus.Unknown, "unknown"), requireCategory: false);

        StagedRecord.DeriveStatus(MatchResult.New, issues)
            .Should().Be(StagedRecordStatus.NeedsReview);
    }

    [Fact]
    public void Denied_Images_Do_Not_Stop_The_Text_Being_Imported()
    {
        var issues = RecordValidator.Validate(
            Complete(), new RightsDecision(RightsStatus.ImagesDenied, "policy:x"), false);

        issues.Should().Contain(issue =>
            issue.Code == ValidationCodes.RightsImagesDenied
            && issue.Severity == IssueSeverity.Warning);

        StagedRecord.DeriveStatus(MatchResult.New, issues).Should().Be(StagedRecordStatus.Valid);
    }

    private static NormalizedProduct Complete() =>
        DeterministicNormalizer.Normalize(Listing(new JsonObject
        {
            ["name"] = "Kettle",
            ["brand"] = "Acme",
            ["offers"] = new JsonObject { ["price"] = "19.99", ["priceCurrency"] = "SGD" },
        }));

    private static NormalizedProduct Variant(string colour) =>
        DeterministicNormalizer.Normalize(Listing(new JsonObject
        {
            ["name"] = "Kettle",
            ["additionalProperty"] = new JsonObject { ["colour"] = colour },
        }));

    private static RawListing Listing(JsonObject document) => new(
        "https://shop.example.com/p/1",
        "shop.example.com",
        "SKU-1",
        Now,
        ExtractionMethod.JsonLd,
        document,
        null,
        [],
        null,
        "cursor");
}
