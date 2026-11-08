using System.Globalization;

namespace EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;

/// <summary>What the seller is entitled to do with one source's material.</summary>
public enum RightsStatus
{
    /// <summary>Text and images may both be republished.</summary>
    Allowed,

    /// <summary>Nobody has decided. Not a refusal, and not a permission either.</summary>
    Unknown,

    /// <summary>The words may not be republished, so there is nothing worth importing.</summary>
    ContentDenied,

    /// <summary>The text is usable; the photography is not.</summary>
    ImagesDenied,
}

/// <summary>
/// The rights answer for one record, and the label that says where it came from.
/// </summary>
/// <remarks>
/// <para>
/// <c>UsageRightsLabel</c> is kept because "we were allowed to publish this" is a claim that has
/// to survive the decision being changed later. It names the evidence — the policy's domain, or
/// the job whose uploader attested to the rights — rather than merely recording a verdict.
/// </para>
/// <para>
/// A stored policy always wins over a file attestation. The attestation is the word of whoever
/// uploaded a spreadsheet; the policy is the seller's own decision about a supplier, and the
/// person uploading a file must not be able to overrule it by ticking a box.
/// </para>
/// </remarks>
public sealed record RightsDecision(RightsStatus Status, string UsageRightsLabel)
{
    public static RightsDecision For(SourcePolicy? policy, bool fileRightsAttested, long jobId)
    {
        if (policy is not null)
        {
            // Content first: if the words may not be used there is nothing to import, and
            // reporting the image question instead would describe the lesser problem.
            if (!policy.ContentUseAllowed)
                return new RightsDecision(RightsStatus.ContentDenied, Label(policy));

            return new RightsDecision(
                policy.ImageUseAllowed ? RightsStatus.Allowed : RightsStatus.ImagesDenied,
                Label(policy));
        }

        if (fileRightsAttested)
        {
            return new RightsDecision(
                RightsStatus.Allowed,
                $"attested:job:{jobId.ToString(CultureInfo.InvariantCulture)}");
        }

        // No policy and nobody vouching. The record is staged for review rather than refused:
        // an unanswered question is not a "no", and an administrator may well answer it.
        return new RightsDecision(RightsStatus.Unknown, "unknown");
    }

    private static string Label(SourcePolicy policy) => $"policy:{policy.Domain}";
}
