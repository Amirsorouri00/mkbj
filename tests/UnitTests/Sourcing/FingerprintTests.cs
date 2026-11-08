using System.Text.Json.Nodes;
using AwesomeAssertions;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using Xunit;

namespace EwaysVNext.UnitTests.Sourcing;

/// <summary>
/// What makes two listings the same product.
/// </summary>
/// <remarks>
/// Get this too narrow and every re-import duplicates the catalogue; too wide and two different
/// products collapse into one. Both failures are expensive and neither is obvious from looking
/// at the code, which is why the cases are spelled out.
/// </remarks>
public sealed class FingerprintTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void One_Shops_Own_Product_Id_Beats_The_Url_It_Was_Linked_From() =>
        // Two pages on one site carrying the same sku are one product, however differently the
        // shop happens to link to them.
        Fingerprint.For(Web("https://a.example/p?id=1", "a.example", "SKU-1"))
            .Should().Be(Fingerprint.For(Web("https://a.example/other", "a.example", "SKU-1")));

    [Fact]
    public void The_Same_Shops_Different_Products_Stay_Different() =>
        Fingerprint.For(Web("https://a.example/p", "a.example", "SKU-1"))
            .Should().NotBe(Fingerprint.For(Web("https://a.example/p", "a.example", "SKU-2")));

    [Fact]
    public void One_Sku_On_Two_Different_Shops_Is_Two_Products() =>
        // Sku namespaces are per shop. Treating them as global would merge unrelated products.
        Fingerprint.For(Web("https://a.example/p", "a.example", "SKU-1"))
            .Should().NotBe(Fingerprint.For(Web("https://b.example/p", "b.example", "SKU-1")));

    [Fact]
    public void A_Url_Is_Identified_By_The_Page_Not_By_How_It_Was_Reached() =>
        Fingerprint.For(Web("https://A.example:443/p?b=2&a=1&utm_source=newsletter#reviews", "a.example", null))
            .Should().Be(Fingerprint.For(Web("https://a.example/p?a=1&b=2", "a.example", null)));

    [Fact]
    public void A_File_Row_Is_Identified_By_Its_Contents_Not_Its_Row_Number() =>
        // The synthetic urn carries the job id and row number, so fingerprinting it would make
        // every re-upload of one spreadsheet a fresh set of products.
        Fingerprint.For(FileRow(jobId: 1, row: 5, gtin: "4006381333931"))
            .Should().Be(Fingerprint.For(FileRow(jobId: 2, row: 9, gtin: "4006381333931")));

    [Fact]
    public void A_File_Row_Falls_Back_To_Brand_And_Name_When_It_Has_No_Identifier() =>
        Fingerprint.For(FileRow(1, 1, brand: "Acme", name: "Kettle — 1.7L"))
            .Should().Be(Fingerprint.For(FileRow(2, 7, brand: "acme", name: "kettle 1.7l")));

    [Fact]
    public void Different_Products_From_One_Brand_Stay_Different() =>
        Fingerprint.For(FileRow(1, 1, brand: "Acme", name: "Kettle"))
            .Should().NotBe(Fingerprint.For(FileRow(1, 2, brand: "Acme", name: "Toaster")));

    [Fact]
    public void A_Fingerprint_Is_Sixty_Four_Lower_Hex_Characters() =>
        Fingerprint.For(Web("https://a.example/p", "a.example", null))
            .Value.Should().MatchRegex("^[0-9a-f]{64}$");

    private static RawListing Web(string url, string? domain, string? sourceId) => new(
        url, domain, sourceId, Now, ExtractionMethod.JsonLd,
        new JsonObject { ["name"] = "A kettle" }, null, [], null, "cursor");

    private static RawListing FileRow(
        long jobId, int row, string? gtin = null, string? brand = null, string? name = null)
    {
        var document = new JsonObject();
        if (gtin is not null) document["gtin13"] = gtin;
        if (brand is not null) document["brand"] = brand;
        if (name is not null) document["name"] = name;

        return new RawListing(
            $"urn:eways:import:{jobId}:row:{row}",
            null,
            null,
            Now,
            ExtractionMethod.Csv,
            document,
            null,
            [],
            null,
            row.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
