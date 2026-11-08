using System.Text.Json.Nodes;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;

namespace EwaysVNext.Modules.Sourcing.Application.Discovery;

/// <summary>How a listing was read out of its source.</summary>
public enum ExtractionMethod
{
    JsonLd = 1,
    Microdata = 2,
    OpenGraph = 3,
    Csv = 4,
    Xlsx = 5,
    Json = 6,
    None = 7,
}

/// <summary>
/// One product-shaped record as found at the source, before any normalization.
/// </summary>
/// <remarks>
/// <para>
/// Every adapter — crawler, search, CSV, spreadsheet — produces this and nothing else. The
/// pipeline behind it never learns where a record came from, which is what lets a file import
/// and a crawl be tested, reasoned about and fixed as one thing.
/// </para>
/// <para>
/// <c>Document</c> is schema.org Product-shaped, and every provenance path an enrichment or a
/// validator records is an RFC 6901 pointer into it. <c>Original</c> is the untouched source
/// node, kept for audit: when somebody disputes what was published, the answer is what the
/// source actually said, not what this system made of it.
/// </para>
/// </remarks>
public sealed record RawListing(
    string SourceUrl,
    string? SourceDomain,
    string? SourceId,
    DateTimeOffset CollectedAt,
    ExtractionMethod Method,
    JsonObject Document,
    JsonObject? Original,
    IReadOnlyList<string> ImageUrls,
    string? ExtractionError,
    string Checkpoint);

/// <summary>What an adapter needs to know to run one job.</summary>
/// <remarks>
/// A flattened copy of the aggregate rather than the aggregate itself: an adapter that held an
/// <see cref="ImportJob"/> could change its state, and adapters have no business doing that.
/// </remarks>
public sealed record ImportJobSpec(
    long JobId,
    ImportMode Mode,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Queries,
    IReadOnlyList<string> IncludeDomains,
    IReadOnlyList<string> ExcludeDomains,
    ImportLimits Limits,
    long? TargetCategoryId,
    bool FileRightsAttested,
    string? FileObjectKey,
    ImportFileFormat? FileFormat,
    ImportColumnMapping? Mapping);
