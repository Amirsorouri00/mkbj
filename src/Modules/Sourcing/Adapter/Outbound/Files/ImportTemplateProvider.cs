using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClosedXML.Excel;
using Mkbj.Database;
using Mkbj.Modules.Sourcing.Application.Discovery;
using Mkbj.Modules.Sourcing.Application.Ports.Outbound;
using Microsoft.EntityFrameworkCore;

namespace Mkbj.Modules.Sourcing.Adapter.Outbound.Files;

internal sealed record ImportFieldGuide(
    string Name, bool Required, string Type, string Description, string Example);

internal sealed class ImportTemplateProvider(MkbjDbContext? db = null) : IImportTemplateProvider
{
    private const int LastInputRow = 1001;
    private const string HeaderOrange = "F28C00";
    private const string HeaderText = "171717";

    private static readonly ImportFieldGuide[] Fields =
    [
        new(ImportColumns.GroupKey, false, "Text", "Groups multiple variant rows into one product.", "DRILL-18V"),
        new(ImportColumns.Title, true, "Text", "Customer-facing product title.", "Acme 18V Drill"),
        new(ImportColumns.Brand, true, "Text", "Brand name; created when it does not exist.", "Acme"),
        new(ImportColumns.CategoryPath, false, "Category path", "Category names separated by > from root to leaf.", "Tools > Power tools"),
        new(ImportColumns.ShortDescription, false, "Text", "Compact summary for cards and search.", "Compact cordless drill"),
        new(ImportColumns.Description, false, "Text", "Full product description.", "Includes battery and charger"),
        new(ImportColumns.Gtin, false, "Digits as text", "GTIN identifier. Keep leading zeroes.", ""),
        new(ImportColumns.Upc, false, "12 digits as text", "UPC-A identifier. Keep leading zeroes.", "012345678905"),
        new(ImportColumns.Ean, false, "13 digits as text", "EAN-13 identifier. Keep leading zeroes.", ""),
        new(ImportColumns.Mpn, false, "Text", "Manufacturer part number.", "ACM-D18"),
        new(ImportColumns.Sku, false, "Text", "Supplier or seller stock code.", "ACM-D18-RED"),
        new(ImportColumns.SourceUrl, false, "Absolute URL", "Product source used for provenance.", "https://supplier.example/products/acm-d18"),
        new(ImportColumns.SourceDomain, false, "Domain", "Source host without a path.", "supplier.example"),
        new(ImportColumns.SourceId, false, "Text", "Stable record identifier at the source.", "acm-d18-red"),
        new(ImportColumns.Price, true, "Decimal >= 0", "Unit price using a dot as decimal separator.", "129.90"),
        new(ImportColumns.Currency, true, "Platform currency", "Choose an enabled platform currency from the dropdown.", "SGD"),
        new(ImportColumns.TaxIncluded, false, "Boolean", "Whether price already includes tax: true or false.", "true"),
        new(ImportColumns.Availability, false, "Availability", "Supplier availability state.", "InStock"),
        new(ImportColumns.ImageUrls, true, "URL list", "Absolute image URLs separated by |. The first is the primary image.", "https://supplier.example/images/acm-d18-1.jpg|https://supplier.example/images/acm-d18-2.jpg"),
        new("attr:color", false, "Attribute text", "Example custom catalogue attribute. Add more attr:<key> columns as needed.", "Red"),
    ];

    private static readonly string[] Columns = [.. Fields.Select(field => field.Name)];
    private static readonly string[] NullableStringTypes = ["string", "null"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<ImportTemplateFile> GetAsync(
        string format, CancellationToken ct = default)
    {
        var currencies = format == "xlsx" ? await EnabledCurrenciesAsync(ct) : [];

        return format switch
        {
            "csv" => new(Csv(), "text/csv; charset=utf-8", "mkbj-products-template.csv"),
            "xlsx" => new(Xlsx(currencies),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "mkbj-products-template.xlsx"),
            "json-schema" => new(Encoding.UTF8.GetBytes(JsonSchema()),
                "application/schema+json; charset=utf-8", "mkbj-products.schema.json"),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private async Task<IReadOnlyList<string>> EnabledCurrenciesAsync(CancellationToken ct)
    {
        if (db is null) return ["USD"];

        return await db.Currencies.AsNoTracking()
            .Where(currency => currency.IsEnabled)
            .OrderBy(currency => currency.Code)
            .Select(currency => currency.Code)
            .ToListAsync(ct);
    }

    private static byte[] Csv()
    {
        var rows = new[] { Columns, SampleValues() };
        var text = string.Join("\r\n", rows.Select(row => string.Join(',', row.Select(Escape)))) + "\r\n";
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(text);
    }

    private static byte[] Xlsx(IReadOnlyList<string> enabledCurrencies)
    {
        using var workbook = new XLWorkbook();
        var products = workbook.AddWorksheet("Products");
        var instructions = workbook.AddWorksheet("Instructions");
        var lists = workbook.AddWorksheet("Lists");
        var sample = SampleValues();

        for (var column = 1; column <= Fields.Length; column++)
        {
            var field = Fields[column - 1];
            var header = products.Cell(1, column);
            header.Value = field.Name;
            header.Style.Font.Bold = true;
            header.Style.Font.FontColor = XLColor.FromHtml(HeaderText);
            header.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderOrange);
            header.CreateComment().AddText(
                $"{(field.Required ? "REQUIRED" : "OPTIONAL")} • {field.Type}\n{field.Description}");
            products.Cell(2, column).Value = sample[column - 1];
        }

        products.Row(1).Height = 28;
        products.SheetView.FreezeRows(1);
        products.Range(1, 1, LastInputRow, Fields.Length).SetAutoFilter();
        products.Range(2, 1, LastInputRow, Fields.Length).Style.Protection.Locked = false;
        products.Columns().AdjustToContents(8, 42);

        AddLists(lists, enabledCurrencies);
        ConfigureFieldFormats(products, lists, enabledCurrencies);
        AddInstructions(instructions);

        products.Protect(XLSheetProtectionElements.SelectUnlockedCells
                         | XLSheetProtectionElements.AutoFilter
                         | XLSheetProtectionElements.Sort
                         | XLSheetProtectionElements.InsertRows);
        instructions.Protect();
        lists.Visibility = XLWorksheetVisibility.VeryHidden;
        products.SetTabColor(XLColor.FromHtml(HeaderOrange));
        instructions.SetTabColor(XLColor.FromHtml("1C1C1C"));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void ConfigureFieldFormats(
        IXLWorksheet sheet, IXLWorksheet lists, IReadOnlyList<string> currencies)
    {
        for (var column = 1; column <= Fields.Length; column++)
        {
            var field = Fields[column - 1];
            var range = sheet.Range(2, column, LastInputRow, column);
            range.Style.NumberFormat.Format = field.Name == ImportColumns.Price ? "0.00" : "@";

            if (!field.Required
                && field.Name is not ImportColumns.TaxIncluded
                && field.Name is not ImportColumns.Availability)
                continue;

            var validation = range.CreateDataValidation();
            validation.IgnoreBlanks = !field.Required;
            validation.ShowErrorMessage = true;
            validation.ErrorTitle = $"Invalid {field.Name}";
            validation.ErrorMessage = field.Required
                ? $"{field.Name} is required and must be {field.Type}."
                : $"Leave blank or enter {field.Type}.";
            validation.ShowInputMessage = true;
            validation.InputTitle = field.Required ? $"Required • {field.Type}" : $"Optional • {field.Type}";
            validation.InputMessage = field.Description;

            if (field.Name == ImportColumns.Currency)
                validation.List(lists.Range(1, 1, Math.Max(currencies.Count, 1), 1));
            else if (field.Name == ImportColumns.TaxIncluded)
                validation.List(lists.Range(1, 2, 2, 2));
            else if (field.Name == ImportColumns.Availability)
                validation.List(lists.Range(1, 3, 5, 3));
            else if (field.Name == ImportColumns.Price)
                validation.Custom($"=AND(ISNUMBER({sheet.Cell(2, column).Address}),{sheet.Cell(2, column).Address}>=0)");
            else
                validation.Custom($"=LEN(TRIM({sheet.Cell(2, column).Address}))>0");
        }
    }

    private static void AddInstructions(IXLWorksheet sheet)
    {
        sheet.Cell("A1").Value = "MKBJ product import template";
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("A1").Style.Font.FontSize = 18;
        sheet.Cell("A2").Value = "Enter products on the Products sheet. Do not rename, remove or reorder its header cells.";
        sheet.Cell("A3").Value = "Orange headers are locked. Required fields reject blank values; dropdown fields use current platform values.";

        var guideHeaders = new[] { "Column", "Requirement", "Type", "How to fill it", "Example" };
        for (var column = 1; column <= guideHeaders.Length; column++)
        {
            var cell = sheet.Cell(5, column);
            cell.Value = guideHeaders[column - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.FromHtml(HeaderText);
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderOrange);
        }

        for (var index = 0; index < Fields.Length; index++)
        {
            var field = Fields[index];
            var row = index + 6;
            sheet.Cell(row, 1).Value = field.Name;
            sheet.Cell(row, 2).Value = field.Required ? "Required" : "Optional";
            sheet.Cell(row, 3).Value = field.Type;
            sheet.Cell(row, 4).Value = field.Description;
            sheet.Cell(row, 5).Value = field.Example;
            if (field.Required) sheet.Cell(row, 2).Style.Font.Bold = true;
        }

        sheet.SheetView.FreezeRows(5);
        sheet.Range(5, 1, Fields.Length + 5, guideHeaders.Length).SetAutoFilter();
        sheet.Column(1).Width = 24;
        sheet.Column(2).Width = 14;
        sheet.Column(3).Width = 22;
        sheet.Column(4).Width = 58;
        sheet.Column(5).Width = 48;
        sheet.Columns(1, 5).Style.Alignment.WrapText = true;
    }

    private static void AddLists(IXLWorksheet sheet, IReadOnlyList<string> currencies)
    {
        var safeCurrencies = currencies.Count > 0 ? currencies : ["USD"];
        for (var index = 0; index < safeCurrencies.Count; index++)
            sheet.Cell(index + 1, 1).Value = safeCurrencies[index];

        sheet.Cell(1, 2).Value = "true";
        sheet.Cell(2, 2).Value = "false";

        var availability = new[] { "InStock", "OutOfStock", "PreOrder", "BackOrder", "Discontinued" };
        for (var index = 0; index < availability.Length; index++)
            sheet.Cell(index + 1, 3).Value = availability[index];
    }

    private static string JsonSchema()
    {
        var properties = ImportColumns.All.ToDictionary(
            field => field,
            _ => (object)new Dictionary<string, object> { ["type"] = NullableStringTypes });

        var schema = new Dictionary<string, object>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "MKBJ product import",
            ["type"] = "array",
            ["items"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["patternProperties"] = new Dictionary<string, object>
                {
                    ["^attr:[a-z0-9_]+$"] = new Dictionary<string, object>
                    {
                        ["type"] = NullableStringTypes,
                    },
                },
                ["required"] = ImportColumns.RequiredForPreview,
                ["additionalProperties"] = false,
            },
        };

        return JsonSerializer.Serialize(schema, JsonOptions);
    }

    private static string[] SampleValues() => [.. Fields.Select(field => field.Example)];

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
