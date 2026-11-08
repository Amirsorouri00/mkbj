using System.Text.Json;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Matching;
using EwaysVNext.Modules.Sourcing.Application.Normalization;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Modules.Sourcing.Application.Validation;

namespace EwaysVNext.Modules.Sourcing.Application.Pipeline;

/// <summary>
/// Everything that happens to one staged record between being found and being publishable.
/// </summary>
/// <remarks>
/// <para>
/// Normalize, decide the rights, match, validate. The order matters: rights are settled before
/// matching because a record whose content may not be used is not worth matching, and matching
/// comes before validation because an uncertain match is itself a reason for review.
/// </para>
/// <para>
/// The only IO is the catalogue lookup and the policy read. Everything else is the pure code
/// tested elsewhere, which is why a record can be re-processed years later and reach the same
/// conclusion.
/// </para>
/// </remarks>
public sealed class RecordProcessor(ICatalogMatcher catalog, ISourcePolicyRepository policies)
{
    public async Task<ProcessedRecord> ProcessAsync(
        StagedRecord record,
        RawListing listing,
        bool fileRightsAttested,
        long jobId,
        bool requireCategory,
        long? adminChoice,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(listing);

        var product = DeterministicNormalizer.Normalize(listing);

        var policy = listing.SourceDomain is { } domain
            ? await policies.FindByDomainAsync(domain, ct)
            : null;

        var rights = RightsDecision.For(policy, fileRightsAttested, jobId);

        var match = await RecordMatcher.MatchAsync(product, catalog, adminChoice, ct);

        var issues = new List<ValidationIssue>(
            RecordValidator.Validate(product, rights, requireCategory));

        // Needs the raw text: by now an invalid barcode has already been dropped, and the record
        // would look as though the source never had one.
        var identifier = RecordValidator.IdentifierIssue(RawGtin(listing), product.Gtin);
        if (identifier is not null) issues.Add(identifier);

        if (listing.ExtractionError is { } extraction)
            issues.Add(new ValidationIssue(extraction, IssueSeverity.Error));

        return new ProcessedRecord(product, rights, match, issues);
    }

    /// <summary>What the source wrote in any GTIN field, valid or not.</summary>
    private static string? RawGtin(RawListing listing)
    {
        foreach (var key in new[] { "gtin", "gtin14", "gtin13", "gtin12", "gtin8" })
        {
            if (listing.Document[key] is { } node) return node.ToString();
        }

        return null;
    }
}

/// <summary>The verdict on one record, before anything is written.</summary>
public sealed record ProcessedRecord(
    NormalizedProduct Product,
    RightsDecision Rights,
    MatchResult Match,
    IReadOnlyList<ValidationIssue> Issues)
{
    public StagedRecordStatus Status => StagedRecord.DeriveStatus(Match, Issues);

    /// <summary>The normalized product as stored on the record, for a reviewer to read.</summary>
    public string ToJson() => JsonSerializer.Serialize(Product);
}
