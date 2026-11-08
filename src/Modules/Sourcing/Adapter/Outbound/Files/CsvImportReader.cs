using System.Text;
using EwaysVNext.Modules.Sourcing.Application;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sourcing.Adapter.Outbound.Files;

/// <summary>
/// Reads a comma-separated file, including the parts people get wrong.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than taken from a library, because the format is small and the failure
/// modes are specific: a supplier's export routinely contains quoted commas, doubled quotes,
/// newlines inside a quoted cell, a UTF-8 byte-order mark on the first heading, and a ragged
/// final row. Each of those has a test.
/// </para>
/// <para>
/// Everything is read as text. Deciding what a cell <b>means</b> is normalization's job, and a
/// reader that parsed numbers would have to guess a culture — which is how "1,5" becomes
/// fifteen.
/// </para>
/// </remarks>
internal sealed class CsvImportReader : IImportFileReader
{
    public bool Handles(ImportFileFormat format) => format == ImportFileFormat.Csv;

    public async Task<Result<ImportFileContents>> ReadAsync(
        Stream content, int maxRows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // StreamReader removes the preamble of the encoding it is given, so a UTF-8 BOM never
        // reaches the first heading — which would otherwise be "﻿name", match no mapping,
        // and silently drop a column. The detect flag only governs switching to a *different*
        // encoding; a test pins the stripping itself, because it is the behaviour that matters
        // and not the flag.
        using var reader = new StreamReader(
            content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var text = await reader.ReadToEndAsync(ct);
        var records = Parse(text);

        if (records.Count == 0)
            return Result.Failure<ImportFileContents>(SourcingErrors.FileEmpty);

        var columns = records[0];

        if (columns.Count == 0 || columns.All(string.IsNullOrWhiteSpace))
            return Result.Failure<ImportFileContents>(SourcingErrors.FileHeadingsMissing);

        var duplicate = columns
            .GroupBy(column => column, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            // Two columns of one name make the mapping ambiguous, and whichever won would be
            // decided by iteration order rather than by anybody.
            return Result.Failure<ImportFileContents>(
                SourcingErrors.FileHeadingDuplicate(duplicate.Key));
        }

        var rows = new List<ImportFileRow>();

        for (var index = 1; index < records.Count && rows.Count < maxRows; index++)
        {
            var values = records[index];

            // A trailing newline is not a row, and neither is a line of empty cells.
            if (values.All(string.IsNullOrWhiteSpace)) continue;

            var cells = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (var column = 0; column < columns.Count; column++)
            {
                // Ragged rows are normal in hand-edited exports: a short row means the trailing
                // columns are empty, not that the file is broken.
                cells[columns[column]] = column < values.Count ? values[column] : null;
            }

            rows.Add(new ImportFileRow(index + 1, cells));
        }

        return Result.Success(new ImportFileContents(columns, rows));
    }

    /// <summary>RFC 4180, with the leniencies real exports need.</summary>
    private static List<List<string>> Parse(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];

            if (quoted)
            {
                if (ch != '"')
                {
                    cell.Append(ch);
                    continue;
                }

                // A doubled quote inside a quoted cell is one literal quote.
                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                    continue;
                }

                quoted = false;
                continue;
            }

            switch (ch)
            {
                case '"':
                    quoted = true;
                    break;

                case ',':
                    record.Add(cell.ToString());
                    cell.Clear();
                    break;

                case '\r':
                    break; // handled by the \n that follows

                case '\n':
                    record.Add(cell.ToString());
                    cell.Clear();
                    records.Add(record);
                    record = [];
                    break;

                default:
                    cell.Append(ch);
                    break;
            }
        }

        // Whatever is left when the text runs out is the last record, unless the file ended
        // with a newline and there is nothing pending.
        if (cell.Length > 0 || record.Count > 0)
        {
            record.Add(cell.ToString());
            records.Add(record);
        }

        return records;
    }
}
