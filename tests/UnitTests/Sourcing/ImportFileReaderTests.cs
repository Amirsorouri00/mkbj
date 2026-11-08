using System.Text;
using AwesomeAssertions;
using EwaysVNext.Modules.Sourcing.Adapter.Outbound.Files;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using Xunit;

namespace EwaysVNext.UnitTests.Sourcing;

/// <summary>
/// Reading the files suppliers actually send.
/// </summary>
/// <remarks>
/// Every case here is something a real export does: quoted commas, doubled quotes, a newline
/// inside a cell, a byte-order mark on the first heading, a ragged last row. None of them is
/// exotic, and each one silently loses data if it is got wrong.
/// </remarks>
public sealed class ImportFileReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_Quoted_Comma_Is_Part_Of_The_Cell()
    {
        var contents = await ReadCsv("name,price\n\"Kettle, 1.7L\",19.99\n");

        contents.Rows.Should().ContainSingle()
            .Which.Cells["name"].Should().Be("Kettle, 1.7L");
    }

    [Fact]
    public async Task A_Doubled_Quote_Is_One_Quote()
    {
        var contents = await ReadCsv("name\n\"The \"\"Big\"\" Kettle\"\n");

        contents.Rows.Single().Cells["name"].Should().Be("The \"Big\" Kettle");
    }

    [Fact]
    public async Task A_Newline_Inside_A_Quoted_Cell_Does_Not_End_The_Row()
    {
        var contents = await ReadCsv("name,description\nKettle,\"Line one\nLine two\"\n");

        contents.Rows.Should().ContainSingle();
        contents.Rows[0].Cells["description"].Should().Be("Line one\nLine two");
    }

    [Fact]
    public async Task A_Byte_Order_Mark_Does_Not_Become_Part_Of_The_First_Heading()
    {
        // Every spreadsheet on Windows writes one, and "﻿name" matches no mapping — so the
        // first column would be silently dropped. This pins the behaviour rather than the
        // StreamReader flag: toggling detectEncodingFromByteOrderMarks does not change it,
        // because the reader strips the preamble of whatever encoding it was given.
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("name,price\nKettle,19.99\n")).ToArray();

        var contents = await Read(new CsvImportReader(), bytes, ImportFileFormat.Csv);

        contents.Columns.Should().Equal("name", "price");
    }

    [Fact]
    public async Task A_Short_Row_Means_Empty_Trailing_Cells_Not_A_Broken_File()
    {
        var contents = await ReadCsv("name,price,currency\nKettle,19.99\n");

        var cells = contents.Rows.Single().Cells;
        cells["price"].Should().Be("19.99");
        cells["currency"].Should().BeNull();
    }

    [Fact]
    public async Task A_Trailing_Newline_Is_Not_A_Row()
    {
        var contents = await ReadCsv("name\nKettle\n\n");

        contents.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Row_Numbers_Are_The_Lines_A_Reviewer_Will_Look_At()
    {
        var contents = await ReadCsv("name\nFirst\nSecond\n");

        // Line 1 is the headings, so the first product is on line 2.
        contents.Rows.Select(row => row.Number).Should().Equal(2, 3);
    }

    [Fact]
    public async Task Two_Columns_Of_One_Name_Are_Refused()
    {
        var result = await new CsvImportReader().ReadAsync(
            Stream(Encoding.UTF8.GetBytes("name,price,name\na,1,b\n")), 100, Ct);

        // Whichever won would be decided by iteration order rather than by anybody.
        result.Error!.Code.Should().Be("IMPORT_FILE_HEADING_DUPLICATE");
    }

    [Fact]
    public async Task An_Empty_File_Is_Refused()
    {
        var result = await new CsvImportReader().ReadAsync(Stream([]), 100, Ct);

        result.Error!.Code.Should().Be("IMPORT_FILE_EMPTY");
    }

    [Fact]
    public async Task Only_The_Rows_Asked_For_Are_Read()
    {
        var csv = "name\n" + string.Concat(Enumerable.Range(1, 50).Select(i => $"P{i}\n"));

        var contents = await Read(new CsvImportReader(), Encoding.UTF8.GetBytes(csv),
            ImportFileFormat.Csv, maxRows: 10);

        contents.Rows.Should().HaveCount(10);
    }

    [Fact]
    public async Task Json_Flattens_To_The_Same_Shape_As_A_Spreadsheet()
    {
        var contents = await Read(
            new JsonImportReader(),
            Encoding.UTF8.GetBytes("""[{"name":"Kettle","price":19.99},{"name":"Toaster","brand":"Acme"}]"""),
            ImportFileFormat.Json);

        // The union of every object's keys, in first-seen order: a real export omits the fields
        // an item has nothing for.
        contents.Columns.Should().Equal("name", "price", "brand");

        contents.Rows[0].Cells["price"].Should().Be("19.99",
            "a number kept as a number would take a different path through normalization");
        contents.Rows[1].Cells["brand"].Should().Be("Acme");
    }

    [Fact]
    public async Task Json_That_Is_Not_An_Array_Of_Objects_Is_Refused()
    {
        var result = await new JsonImportReader().ReadAsync(
            Stream(Encoding.UTF8.GetBytes("""{"name":"Kettle"}""")), 100, Ct);

        result.Error!.Code.Should().Be("IMPORT_FILE_MALFORMED");
    }

    [Fact]
    public async Task Malformed_Json_Is_Refused_With_Something_To_Act_On()
    {
        var result = await new JsonImportReader().ReadAsync(
            Stream(Encoding.UTF8.GetBytes("[{")), 100, Ct);

        result.Error!.Code.Should().Be("IMPORT_FILE_MALFORMED");
        result.Error.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Each_Reader_Claims_Only_Its_Own_Format()
    {
        new CsvImportReader().Handles(ImportFileFormat.Csv).Should().BeTrue();
        new CsvImportReader().Handles(ImportFileFormat.Json).Should().BeFalse();
        new JsonImportReader().Handles(ImportFileFormat.Json).Should().BeTrue();
        new JsonImportReader().Handles(ImportFileFormat.Xlsx).Should().BeFalse();
    }

    private static Task<ImportFileContents> ReadCsv(string csv) =>
        Read(new CsvImportReader(), Encoding.UTF8.GetBytes(csv), ImportFileFormat.Csv);

    private static async Task<ImportFileContents> Read(
        IImportFileReader reader, byte[] bytes, ImportFileFormat format, int maxRows = 1_000)
    {
        reader.Handles(format).Should().BeTrue();

        var result = await reader.ReadAsync(Stream(bytes), maxRows, Ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);

        return result.Value!;
    }

    private static MemoryStream Stream(byte[] bytes) => new(bytes);
}
