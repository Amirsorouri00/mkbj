using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Settings.Application.Settings.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Settings;

public sealed class SettingsDomainTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;

    private static string CodeOf(Action act) =>
        FluentActions.Invoking(act).Should().Throw<DomainException>().Which.Code;

    private static void UpdateSeller(SellerProfile profile, bool gstRegistered = false, params string[] rules) =>
        profile.Update("Acme Pte Ltd", "202012345A", "1 Raffles Place", "billing@acme.sg",
            gstRegistered, "Thank you", rules.Length == 0 ? ["email|phone"] : rules);

    [Fact]
    public void Seller_Refuses_Gst_Registration() =>
        CodeOf(() => UpdateSeller(SellerProfile.CreateDefault(), gstRegistered: true))
            .Should().Be("VALIDATION_FAILED",
                "the tax policy returns zero, so a GST-registered profile would under-report");

    [Theory]
    [InlineData("email|fax")]
    [InlineData("")]
    [InlineData("email||phone")]
    [InlineData("   ")]
    public void Unknown_Or_Empty_Buyer_Field_Rules_Are_Refused(string rule) =>
        CodeOf(() => UpdateSeller(SellerProfile.CreateDefault(), rules: rule))
            .Should().Be("VALIDATION_FAILED");

    [Fact]
    public void Buyer_Field_Rules_Accept_Known_Alternatives() =>
        FluentActions.Invoking(() =>
                UpdateSeller(SellerProfile.CreateDefault(), rules: ["email|phone", "companyName"]))
            .Should().NotThrow();

    [Theory]
    [InlineData("email|phone", "email", true)]
    [InlineData("email|phone", "phone", true)]
    [InlineData("email|phone", "companyName", false)]
    public void A_Rule_Is_Satisfied_By_Any_Of_Its_Alternatives(string rule, string supplied, bool satisfied) =>
        BuyerFieldRules.IsSatisfiedBy([rule], new HashSet<string>(StringComparer.Ordinal) { supplied })
            .Should().Be(satisfied);

    [Fact]
    public void The_Default_Profile_Is_Obviously_Unconfigured() =>
        SellerProfile.CreateDefault().LegalName.Should().Be(SettingsDefaults.LegalName,
            "a placeholder nobody notices is worse than no value at all");

    [Fact]
    public void Publish_Appends_Next_Version_And_Activates_It()
    {
        var instruction = PaymentInstruction.CreateDefault(Now);
        var originalText = instruction.Active.Text;

        var published = instruction.Publish(2, "Pay by cheque.", Guid.NewGuid(), Now);

        instruction.Versions.Select(version => version.Version).Should().Equal([1, 2]);
        instruction.ActiveVersion.Should().Be(2);
        published.Text.Should().Be("Pay by cheque.");

        instruction.Versions.Single(version => version.Version == 1).Text.Should().Be(originalText,
            "an invoice already issued quotes the version it was issued under");
    }

    [Fact]
    public void Activate_Returns_To_An_Earlier_Version()
    {
        var instruction = PaymentInstruction.CreateDefault(Now);
        instruction.Publish(2, "Pay by cheque.", Guid.NewGuid(), Now);

        instruction.Activate(1);

        instruction.ActiveVersion.Should().Be(1);
        instruction.Active.Text.Should().Be(SettingsDefaults.InitialPaymentInstructionText);
    }

    [Fact]
    public void Activate_Unknown_Version_Is_Not_Found() =>
        CodeOf(() => PaymentInstruction.CreateDefault(Now).Activate(99)).Should().Be("NOT_FOUND");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Publish_Refuses_Empty_Text(string text) =>
        CodeOf(() => PaymentInstruction.CreateDefault(Now).Publish(2, text, Guid.NewGuid(), Now))
            .Should().Be("VALIDATION_FAILED");

    [Fact]
    public void Publish_Refuses_Text_Past_The_Limit() =>
        CodeOf(() => PaymentInstruction.CreateDefault(Now)
                .Publish(2, new string('x', 4001), Guid.NewGuid(), Now))
            .Should().Be("VALIDATION_FAILED");

    [Theory]
    [InlineData(1000, 1000, 100, 8000, 200)]
    [InlineData(0, 10, 100, 8000, 200)]
    [InlineData(1000, 100_000_000, 0, 8000, 200)]
    [InlineData(1000, 100_000_000, 1001, 8000, 200)]
    [InlineData(1000, 100_000_000, 100, 9501, 200)]
    [InlineData(1000, 100_000_000, 100, 999, 200)]
    [InlineData(1000, 100_000_000, 100, 8000, 0)]
    [InlineData(1000, 100_000_000, 100, 8000, 1001)]
    public void CommerceLimits_Bounds_Are_Enforced(
        long min, long max, int bulk, int deadline, int maxLines) =>
        CodeOf(() => CommerceLimits.CreateDefault().Update(min, max, bulk, deadline, maxLines))
            .Should().Be("VALIDATION_FAILED");

    [Fact]
    public void CommerceLimits_Defaults_Match_The_Spec()
    {
        var limits = CommerceLimits.CreateDefault();

        limits.MinUsdMinor.Should().Be(1_000);
        limits.MaxUsdMinor.Should().Be(100_000_000);
        limits.BulkDefaultMax.Should().Be(100);
        limits.CompositionDeadlineMs.Should().Be(8000);
        limits.CompositionMaxLines.Should().Be(200);
    }

    [Fact]
    public void The_Composition_Deadline_Stays_Inside_The_Ten_Second_Budget() =>
        CommerceLimits.CreateDefault().CompositionDeadlineMs.Should().BeLessThan(10_000,
            "a deadline at or past the budget never saves the request");

    [Fact]
    public void Every_Singleton_Starts_At_The_Same_Id()
    {
        SellerProfile.CreateDefault().Id.Should().Be(SettingsDefaults.SingletonId);
        CommerceLimits.CreateDefault().Id.Should().Be(SettingsDefaults.SingletonId);
        PaymentInstruction.CreateDefault(Now).Id.Should().Be(SettingsDefaults.SingletonId);
    }
}
