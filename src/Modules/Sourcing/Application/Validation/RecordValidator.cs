using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Normalization;

namespace EwaysVNext.Modules.Sourcing.Application.Validation;

/// <summary>
/// Decides what is wrong with a normalized record, and how badly.
/// </summary>
/// <remarks>
/// <para>
/// Pure, so the same record always produces the same findings and a reviewer can be shown
/// exactly what the pipeline saw. The severities are the whole design: an <c>Error</c> is
/// something no person can wave through, a <c>Review</c> is a decision somebody has to make,
/// and a <c>Warning</c> is recorded and changes nothing.
/// </para>
/// <para>
/// Rights are assessed here rather than at publication, because the answer changes what happens
/// to the record: denied content means there is nothing worth importing, denied images means
/// import the text and drop the pictures, and unknown means ask a person.
/// </para>
/// </remarks>
public static class RecordValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(
        NormalizedProduct product, RightsDecision rights, bool requireCategory)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(rights);

        var issues = new List<ValidationIssue>();

        // Without these there is no product to create at all.
        if (product.Name is null)
            issues.Add(new ValidationIssue(ValidationCodes.TitleMissing, IssueSeverity.Error));

        if (product.PriceMinor is null)
            issues.Add(new ValidationIssue(ValidationCodes.PriceMissing, IssueSeverity.Error));
        else if (product.PriceMinor <= 0)
            issues.Add(new ValidationIssue(ValidationCodes.PriceInvalid, IssueSeverity.Error));

        if (product.Currency is null)
            issues.Add(new ValidationIssue(ValidationCodes.CurrencyMissing, IssueSeverity.Error));

        // A brand is required to publish, but a missing one is a person's decision rather than
        // a dead end: plenty of real products are unbranded, and somebody has to say so.
        if (product.Brand is null)
            issues.Add(new ValidationIssue(ValidationCodes.BrandMissing, IssueSeverity.Review));

        if (requireCategory && product.Category is null)
            issues.Add(new ValidationIssue(ValidationCodes.CategoryMissing, IssueSeverity.Review));

        // A shop page with no picture is a poor listing, not an invalid one.
        if (product.ImageUrls.Count == 0)
            issues.Add(new ValidationIssue(ValidationCodes.ImageMissing, IssueSeverity.Warning));

        issues.AddRange(RightsIssues(rights));

        return issues;
    }

    /// <summary>
    /// A warning that the source had a barcode which did not add up.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Validate"/> because it needs the raw text: by the time
    /// normalization has finished, an invalid GTIN has already been dropped, and the record
    /// would look simply as though the source never had one.
    /// </remarks>
    public static ValidationIssue? IdentifierIssue(string? rawGtin, string? normalizedGtin) =>
        !string.IsNullOrWhiteSpace(rawGtin) && normalizedGtin is null
            ? new ValidationIssue(
                ValidationCodes.IdentifierInvalid, IssueSeverity.Warning, rawGtin)
            : null;

    private static IEnumerable<ValidationIssue> RightsIssues(RightsDecision rights)
    {
        switch (rights.Status)
        {
            case RightsStatus.ContentDenied:
                // An error, not a review item. Nobody in the backoffice can grant a right the
                // supplier withheld.
                yield return new ValidationIssue(
                    ValidationCodes.RightsContentDenied, IssueSeverity.Error, rights.UsageRightsLabel);
                break;

            case RightsStatus.ImagesDenied:
                // The text is usable; the pictures are dropped at publication.
                yield return new ValidationIssue(
                    ValidationCodes.RightsImagesDenied, IssueSeverity.Warning, rights.UsageRightsLabel);
                break;

            case RightsStatus.Unknown:
                // Somebody must take responsibility before this is republished.
                yield return new ValidationIssue(
                    ValidationCodes.RightsUnknown, IssueSeverity.Review, rights.UsageRightsLabel);
                break;

            case RightsStatus.Allowed:
            default:
                break;
        }
    }
}
