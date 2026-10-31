using AwesomeAssertions;
using EwaysVNext.Modules.Customer.Application.Customers.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Customers;

public sealed class PhoneTests
{
    [Theory]
    [InlineData("+65 9123 4567", "+6591234567")]
    [InlineData("+1 (415) 555-0100", "+14155550100")]
    [InlineData("+44.20.7123.4567", "+442071234567")]
    [InlineData("  +6591234567  ", "+6591234567")]
    public void Phone_Normalizes_To_E164(string raw, string expected)
    {
        Phone.TryCreate(raw, out var phone).Should().BeTrue();
        phone!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("91234567")]
    [InlineData("+0123456789")]
    [InlineData("+12345")]
    [InlineData("+1234567890123456")]
    [InlineData("+65abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Phone_Rejects_Non_E164(string? raw) =>
        Phone.TryCreate(raw, out _).Should().BeFalse();

    [Fact]
    public void A_Local_Number_Is_Refused_Rather_Than_Assumed_To_Be_Singapore() =>
        Phone.TryCreate("91234567", out _).Should().BeFalse(
            "the seller sells internationally, and guessing a country code attaches a real "
            + "person's number elsewhere to this customer");

    [Fact]
    public void Two_Spellings_Of_One_Number_Are_Equal()
    {
        Phone.TryCreate("+65 9123 4567", out var spaced);
        Phone.TryCreate("+65-9123-4567", out var dashed);

        spaced.Should().Be(dashed, "a partner sends whichever form their own system stores");
    }
}
