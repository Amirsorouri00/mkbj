using Mkbj.Modules.Sourcing.Application.Domain.ImportJobs;
using Mkbj.Outcomes;

namespace Mkbj.Modules.Sourcing.Application.Ports.Outbound;

/// <summary>One row of an uploaded file, as column heading to cell text.</summary>
public sealed record ImportFileRow(int Number, IReadOnlyDictionary<string, string?> Cells);

/// <summary>
/// What a file turned out to contain.
/// </summary>
/// <remarks>
/// The headings come back separately from the rows because an administrator maps columns
/// <b>before</b> the import runs: the upload answers "what is in this file?", and the mapping
/// screen is built from that answer.
/// </remarks>
public sealed record ImportFileContents(
    IReadOnlyList<string> Columns, IReadOnlyList<ImportFileRow> Rows);

public interface IImportFileReader
{
    /// <summary>Whether this reader handles that format.</summary>
    bool Handles(ImportFileFormat format);

    /// <summary>
    /// Reads the file, or refuses it.
    /// </summary>
    /// <remarks>
    /// A <see cref="Result"/> rather than an exception: a malformed upload is an ordinary thing
    /// for a person to do, and the administrator needs to be told which row was wrong.
    /// </remarks>
    Task<Result<ImportFileContents>> ReadAsync(
        Stream content, int maxRows, CancellationToken ct = default);
}

public sealed record ImportTemplateFile(
    byte[] Content, string ContentType, string FileName);

/// <summary>Produces the exact files accepted by the file-import reader.</summary>
public interface IImportTemplateProvider
{
    Task<ImportTemplateFile> GetAsync(string format, CancellationToken ct = default);
}
