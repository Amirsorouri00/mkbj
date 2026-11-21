using System.Globalization;
using ClosedXML.Excel;
using Mkbj.Modules.Sourcing.Application;
using Mkbj.Modules.Sourcing.Application.Domain.ImportJobs;
using Mkbj.Modules.Sourcing.Application.Ports.Outbound;
using Mkbj.Outcomes;

namespace Mkbj.Modules.Sourcing.Adapter.Outbound.Files;

/// <summary>Reads the first worksheet while preserving the displayed text of every cell.</summary>
internal sealed class XlsxImportReader : IImportFileReader
{
    public bool Handles(ImportFileFormat format) => format == ImportFileFormat.Xlsx;

    public Task<Result<ImportFileContents>> ReadAsync(
        Stream content, int maxRows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ct.ThrowIfCancellationRequested();

        try
        {
            using var workbook = new XLWorkbook(content);
            var sheet = workbook.Worksheets.FirstOrDefault();
            var lastCell = sheet?.LastCellUsed();
            if (sheet is null || lastCell is null)
                return Task.FromResult(Result.Failure<ImportFileContents>(SourcingErrors.FileEmpty));

            var columns = Enumerable.Range(1, lastCell.Address.ColumnNumber)
                .Select(column => sheet.Cell(1, column).GetFormattedString(CultureInfo.InvariantCulture).Trim())
                .ToList();

            if (columns.All(string.IsNullOrWhiteSpace))
                return Task.FromResult(Result.Failure<ImportFileContents>(SourcingErrors.FileHeadingsMissing));

            var duplicate = columns.GroupBy(column => column, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null)
                return Task.FromResult(Result.Failure<ImportFileContents>(
                    SourcingErrors.FileHeadingDuplicate(duplicate.Key)));

            var rows = new List<ImportFileRow>();
            for (var row = 2; row <= lastCell.Address.RowNumber && rows.Count < maxRows; row++)
            {
                ct.ThrowIfCancellationRequested();
                var cells = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                var any = false;

                for (var column = 1; column <= columns.Count; column++)
                {
                    var value = sheet.Cell(row, column)
                        .GetFormattedString(CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(value)) any = true;
                    cells[columns[column - 1]] = string.IsNullOrEmpty(value) ? null : value;
                }

                if (any) rows.Add(new ImportFileRow(row, cells));
            }

            if (rows.Count == 0)
                return Task.FromResult(Result.Failure<ImportFileContents>(SourcingErrors.FileEmpty));

            return Task.FromResult(Result.Success(new ImportFileContents(columns, rows)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(Result.Failure<ImportFileContents>(
                SourcingErrors.FileMalformed(exception.Message)));
        }
    }
}
