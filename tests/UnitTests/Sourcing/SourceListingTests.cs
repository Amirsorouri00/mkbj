using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourceListings;
using Xunit;

namespace EwaysVNext.UnitTests.Sourcing;

/// <summary>
/// What a source has been charging, kept as history rather than as a current value.
/// </summary>
public sealed class SourceListingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_Same_Observation_Twice_Is_Still_One_Observation()
    {
        var listing = Listing();

        var first = listing.Observe(10, "sig", 1_999, "SGD", true, Now);
        var second = listing.Observe(11, "sig", 1_999, "SGD", true, Now);

        // A redelivered step must not turn one price into two and make a flat price look like
        // a flurry of changes.
        second.Should().BeSameAs(first);
        listing.Observations.Should().HaveCount(1);
    }

    [Fact]
    public void A_Later_Price_Is_Appended_Rather_Than_Replacing_The_Old_One()
    {
        var listing = Listing();

        listing.Observe(10, "sig", 1_999, "SGD", null, Now);
        listing.Observe(11, "sig", 1_899, "SGD", null, Now.AddDays(1));

        // The history is the point: overwriting would throw away the only evidence of what the
        // supplier charged last month.
        listing.Observations.Should().HaveCount(2);
    }

    [Fact]
    public void Two_Variants_At_One_Moment_Are_Two_Observations()
    {
        var listing = Listing();

        listing.Observe(10, "red", 1_999, "SGD", null, Now);
        listing.Observe(11, "blue", 2_099, "SGD", null, Now);

        listing.Observations.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_Price_Of_Nothing_Or_Less_Is_Refused(long amountMinor)
    {
        var act = () => Listing().Observe(10, "sig", amountMinor, "SGD", null, Now);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("PRICE_OBSERVATION_AMOUNT_INVALID");
    }

    [Theory]
    [InlineData("sgd")]
    [InlineData("SG")]
    [InlineData("SGDX")]
    public void A_Currency_Is_Three_Upper_Case_Letters(string currency)
    {
        var act = () => Listing().Observe(10, "sig", 1, currency, null, Now);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("PRICE_OBSERVATION_CURRENCY_INVALID");
    }

    [Fact]
    public void A_Listing_Stores_Its_Domain_Normalised() =>
        SourceListing.Create(
            1, "https://x/p", "WWW.Shop.Example", null, "t", [], "policy:shop.example", 1, Now)
            .Domain.Should().Be("shop.example");

    private static SourceListing Listing() => SourceListing.Create(
        1,
        "https://shop.example.com/p/1",
        "shop.example.com",
        "SKU-1",
        "A kettle",
        [],
        "policy:shop.example.com",
        stagedRecordId: 1,
        Now);
}
