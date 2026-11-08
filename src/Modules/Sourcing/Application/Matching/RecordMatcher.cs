using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Normalization;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Sourcing.Application.Matching;

/// <summary>
/// Decides whether a listing is a product the catalogue already has.
/// </summary>
/// <remarks>
/// <para>
/// The order is the design: an identifier is a <b>fact</b> and a title is a <b>suggestion</b>.
/// A GTIN that matches settles the question outright; a title that looks similar is a question
/// for a person, because two genuinely different products from one brand often read almost
/// identically — "Acme Kettle 1.7L" and "Acme Kettle 1.5L" differ by one character.
/// </para>
/// <para>
/// When several titles are close, the result is <c>Ambiguous</c> rather than "the best one".
/// Picking a winner from near-ties is how an import quietly merges two products, and the
/// difference between the top two candidates is exactly the information a reviewer needs.
/// </para>
/// </remarks>
public static class RecordMatcher
{
    /// <summary>Below this, a title is not worth showing a reviewer.</summary>
    public const double SimilarityThreshold = 0.55;

    /// <summary>
    /// How close the runner-up may be before the answer is "ask a person".
    /// </summary>
    /// <remarks>
    /// Two candidates within this of each other are, for practical purposes, equally good — and
    /// choosing between them is a judgement, not a calculation.
    /// </remarks>
    public const double AmbiguityMargin = 0.05;

    public const int MaxCandidates = 5;

    public static async Task<MatchResult> MatchAsync(
        NormalizedProduct product,
        ICatalogMatcher catalog,
        long? adminChoice = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(catalog);

        // An administrator has already answered the question matching exists to answer.
        if (adminChoice is { } chosen)
            return new MatchResult(MatchKind.ExactMatch, chosen, "admin-merge", []);

        if (product.Gtin is { } gtin)
        {
            var byGtin = await catalog.FindByIdentifierAsync("Gtin", gtin, ct);
            if (byGtin is { } productId)
                return new MatchResult(MatchKind.ExactMatch, productId, "gtin", []);
        }

        if (product.Mpn is { } mpn)
        {
            var byMpn = await catalog.FindByIdentifierAsync("Mpn", mpn, ct);
            if (byMpn is { } productId)
                return new MatchResult(MatchKind.ExactMatch, productId, "mpn", []);
        }

        if (product.Name is null) return MatchResult.New;

        var candidates = await catalog.FindSimilarAsync(
            product.Name, SimilarityThreshold, MaxCandidates, ct);

        if (candidates.Count == 0) return MatchResult.New;

        var refs = candidates
            .Select(candidate => new FuzzyCandidateRef(
                candidate.ProductId, candidate.Title, candidate.Similarity))
            .ToList();

        if (candidates.Count > 1
            && candidates[0].Similarity - candidates[1].Similarity < AmbiguityMargin)
        {
            return new MatchResult(MatchKind.Ambiguous, null, "title", refs);
        }

        // One clear front-runner — still a suggestion, never a decision.
        return new MatchResult(MatchKind.Fuzzy, null, "title", refs);
    }
}
