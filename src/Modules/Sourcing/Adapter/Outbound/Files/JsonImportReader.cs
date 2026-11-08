using System.Text.Json;
using EwaysVNext.Modules.Sourcing.Application;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Files;

/// <summary>
/// Reads an array of flat objects.
/// </summary>
/// <remarks>
/// <para>
/// Flattened to the same column-and-cell shape a spreadsheet produces, so one mapping screen and
/// one row mapper serve every file format. A JSON import that kept its nesting would need a
/// mapping language of its own, which is a lot of machinery for a format most suppliers export
/// as a flat list anyway.
/// </para>
/// <para>
/// The columns are the union of every object's keys in first-seen order, because objects in a
/// real export omit the fields they have nothing for.
/// </para>
/// </remarks>
internal sealed class JsonImportReader : IImportFileReader
{
    public bool Handles(ImportFileFormat format) => format == ImportFileFormat.Json;

    public async Task<Result<ImportFileContents>> ReadAsync(
        Stream content, int maxRows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(content, cancellationToken: ct);
        }
        catch (JsonException exception)
        {
            return Result.Failure<ImportFileContents>(
                SourcingErrors.FileMalformed(exception.Message));
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Result.Failure<ImportFileContents>(SourcingErrors.FileMalformed(
                    "The file must be a JSON array of objects, one per product."));
            }

            var columns = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<ImportFileRow>();
            var number = 1;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                number++;
                if (rows.Count >= maxRows) break;

                if (element.ValueKind != JsonValueKind.Object)
                {
                    return Result.Failure<ImportFileContents>(SourcingErrors.FileMalformed(
                        $"Item {number - 1} is not an object."));
                }

                var cells = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                foreach (var property in element.EnumerateObject())
                {
                    if (seen.Add(property.Name)) columns.Add(property.Name);

                    cells[property.Name] = Text(property.Value);
                }

                rows.Add(new ImportFileRow(number, cells));
            }

            if (rows.Count == 0) return Result.Failure<ImportFileContents>(SourcingErrors.FileEmpty);

            return Result.Success(new ImportFileContents(columns, rows));
        }
    }

    /// <remarks>
    /// Everything becomes text, as it would from a spreadsheet. A number kept as a number here
    /// would take a different path through normalization from the identical value in a CSV, and
    /// two paths is one more than can be tested.
    /// </remarks>
    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText(),
    };
}
