using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Customers.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Customers;

public sealed class AddressTests
{
    private static Address Create(string country = "SG", string line1 = "1 Raffles Place") =>
        Address.Create(line1, null, "Singapore", "048616", country);

    private static string CodeOf(Action act) =>
        FluentActions.Invoking(act).Should().Throw<DomainException>().Which.Code;

    [Fact]
    public void Address_Uppercases_The_Country_Code() =>
        Create(country: "sg").CountryIso2.Should().Be("SG");

    [Theory]
    [InlineData("SGP")]
    [InlineData("S")]
    [InlineData("")]
    [InlineData("12")]
    public void A_Country_Code_That_Is_Not_Two_Letters_Is_Refused(string country) =>
        CodeOf(() => Create(country)).Should().Be("VALIDATION_FAILED",
            "the country decides tax and shipping, so it must be exact");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Line1_Is_Required(string line1) =>
        CodeOf(() => Create(line1: line1)).Should().Be("VALIDATION_FAILED");

    [Fact]
    public void Fields_Are_Trimmed() =>
        Address.Create("  1 Raffles Place  ", "  ", "  Singapore  ", " 048616 ", " sg ")
            .Should().BeEquivalentTo(new
            {
                Line1 = "1 Raffles Place",
                Line2 = (string?)null,
                City = "Singapore",
                PostalCode = "048616",
                CountryIso2 = "SG",
            });

    [Fact]
    public void A_Blank_Second_Line_Becomes_Null() =>
        Create().Line2.Should().BeNull("an empty string and an absent line are the same thing");

    [Fact]
    public void Two_Identical_Addresses_Are_Equal() =>
        Create().Should().Be(Create());

    [Fact]
    public void Line1_Past_The_Limit_Is_Refused() =>
        CodeOf(() => Create(line1: new string('x', 201))).Should().Be("VALIDATION_FAILED");
}
