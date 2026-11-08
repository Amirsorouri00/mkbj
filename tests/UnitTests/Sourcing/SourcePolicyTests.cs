using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;
using Xunit;

namespace EwaysVNext.UnitTests.Sourcing;

/// <summary>
/// Whose material may be republished, and on whose authority.
/// </summary>
/// <remarks>
/// This is the one part of importing with legal weight. Everything else can be corrected after
/// the fact; publishing a supplier's copy or photography without the right to do so cannot.
/// </remarks>
public sealed class SourcePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("https://WWW.Shop.Example.com:8443/p/1", "shop.example.com")]
    [InlineData("www.example.com", "example.com")]
    [InlineData("EXAMPLE.com", "example.com")]
    [InlineData("https://example.com/a/b?c=d", "example.com")]
    public void A_Domain_Reduces_To_The_Form_Two_Urls_For_One_Site_Agree_On(
        string input, string expected) =>
        SourceDomain.Normalize(input).Should().Be(expected);

    [Fact]
    public void Only_One_Leading_Www_Is_Stripped() =>
        // www.www.example.com is a different host, and collapsing both would merge two sites'
        // policies into one.
        SourceDomain.Normalize("www.www.example.com").Should().Be("www.example.com");

    [Fact]
    public void No_Policy_Is_Unknown_Rather_Than_Denied() =>
        // An unanswered question is not a refusal. The record is staged for review.
        RightsDecision.For(null, fileRightsAttested: false, jobId: 7).Status
            .Should().Be(RightsStatus.Unknown);

    [Fact]
    public void An_Attestation_Names_The_Job_That_Made_It() =>
        RightsDecision.For(null, fileRightsAttested: true, jobId: 7)
            .Should().Be(new RightsDecision(RightsStatus.Allowed, "attested:job:7"));

    [Fact]
    public void Content_Denied_Is_Reported_Ahead_Of_Images()
    {
        var decision = RightsDecision.For(Policy(content: false, images: false), false, 7);

        // If the words may not be used there is nothing to import at all; naming the image
        // problem instead would describe the lesser one.
        decision.Status.Should().Be(RightsStatus.ContentDenied);
    }

    [Fact]
    public void Images_Can_Be_Denied_While_The_Text_Is_Allowed() =>
        // The common real arrangement: a supplier's catalogue copy may be republished, its
        // photography may not.
        RightsDecision.For(Policy(content: true, images: false), false, 7).Status
            .Should().Be(RightsStatus.ImagesDenied);

    [Fact]
    public void An_Allowed_Decision_Names_The_Policy_It_Came_From() =>
        RightsDecision.For(Policy(content: true, images: true), false, 7)
            .Should().Be(new RightsDecision(RightsStatus.Allowed, "policy:shop.example.com"));

    [Fact]
    public void A_Stored_Policy_Overrules_Whoever_Uploaded_The_File()
    {
        var decision = RightsDecision.For(
            Policy(content: false, images: true), fileRightsAttested: true, jobId: 7);

        // The attestation is the word of whoever uploaded a spreadsheet; the policy is the
        // seller's own decision about a supplier.
        decision.Status.Should().Be(RightsStatus.ContentDenied);
    }

    [Fact]
    public void A_Policy_Needs_A_Domain()
    {
        var act = () => SourcePolicy.Create(1, " ", true, true, true, null, Now);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("SOURCE_POLICY_DOMAIN_REQUIRED");
    }

    [Fact]
    public void Notes_Are_Bounded()
    {
        var act = () => SourcePolicy.Create(
            1, "example.com", true, true, true, new string('x', 2_001), Now);

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("SOURCE_POLICY_NOTES_TOO_LONG");
    }

    [Fact]
    public void A_Policy_Stores_Its_Domain_Normalised() =>
        SourcePolicy.Create(1, "https://WWW.Shop.Example.com/x", true, true, true, null, Now)
            .Domain.Should().Be("shop.example.com");

    private static SourcePolicy Policy(bool content, bool images) =>
        SourcePolicy.Create(
            1, "www.shop.example.com", crawlAllowed: true, content, images, null, Now);
}
