using AwesomeAssertions;
using Mkbj.Modules.Sourcing.Application.Discovery;
using Mkbj.Modules.Sourcing.Application.Domain.ImportJobs;
using Xunit;

namespace Mkbj.UnitTests.Sourcing;

public sealed class ImportRowMapperTests
{
    [Fact]
    public void Planned_columns_map_images_attributes_and_schema_org_fields()
    {
        var mapping = new ImportColumnMapping(new Dictionary<string, string>
        {
            ["Product"] = ImportColumns.Title,
            ["Images"] = ImportColumns.ImageUrls,
            ["Colour"] = "attr:color",
            ["Amount"] = ImportColumns.Price,
            ["Money"] = ImportColumns.Currency,
        });
        var row = new Dictionary<string, string?>
        {
            ["Product"] = "Acme Drill",
            ["Images"] = "https://img.example/a.jpg | https://img.example/b.png",
            ["Colour"] = "Red",
            ["Amount"] = "129.90",
            ["Money"] = "sgd",
        };

        var listing = ImportRowMapper.Map(
            9, 2, row, mapping, ExtractionMethod.Csv, DateTimeOffset.UnixEpoch);

        listing.Document["name"]!.ToString().Should().Be("Acme Drill");
        listing.Document["offers"]!["price"]!.ToString().Should().Be("129.90");
        listing.Document["additionalProperty"]!["color"]!.ToString().Should().Be("Red");
        listing.ImageUrls.Should().Equal(
            "https://img.example/a.jpg", "https://img.example/b.png");
    }
}
