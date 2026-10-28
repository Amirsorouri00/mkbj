using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class MeasurementUnitDomainTests
{
    [Fact]
    public void Create_Uses_Provided_Id_And_Trims_Name_And_Symbol()
    {
        var unit = MeasurementUnit.Create(1, " Kilogram ", " kg ");

        unit.Id.Should().Be(1);
        unit.Name.Should().Be("Kilogram");
        unit.Symbol.Should().Be("kg");
    }

    [Theory]
    [InlineData(0, "Kilogram", "kg", "ID_INVALID")]
    [InlineData(-1, "Kilogram", "kg", "ID_INVALID")]
    [InlineData(1, null, "kg", "NAME_REQUIRED")]
    [InlineData(1, " ", "kg", "NAME_REQUIRED")]
    [InlineData(1, "Kilogram", null, "SYMBOL_REQUIRED")]
    [InlineData(1, "Kilogram", " ", "SYMBOL_REQUIRED")]
    public void Create_Rejects_Invalid_Fields(long id, string? name, string? symbol, string code)
    {
        Action act = () => MeasurementUnit.Create(id, name!, symbol!);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("MEASUREMENT_UNIT_" + code);
    }

    [Fact]
    public void Create_Enforces_Lengths_After_Trimming()
    {
        var name = new string('n', 100);
        var symbol = new string('s', 20);
        var unit = MeasurementUnit.Create(1, " " + name + " ", " " + symbol + " ");
        unit.Name.Should().Be(name);
        unit.Symbol.Should().Be(symbol);

        Action longName = () => MeasurementUnit.Create(1, name + "n", symbol);
        Action longSymbol = () => MeasurementUnit.Create(1, name, symbol + "s");
        longName.Should().Throw<DomainException>().Which.Code.Should().Be("MEASUREMENT_UNIT_NAME_TOO_LONG");
        longSymbol.Should().Throw<DomainException>().Which.Code.Should().Be("MEASUREMENT_UNIT_SYMBOL_TOO_LONG");
    }
}
