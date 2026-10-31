using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Settings.Application.Settings.Domain;

/// <summary>
/// Which details a buyer must supply, expressed as alternatives.
/// <para>
/// Each entry is one requirement; <c>|</c> separates ways of satisfying it, so
/// <c>"email|phone"</c> means "a way to reach them" rather than "an email address". That
/// distinction is the whole reason this is a list of rules and not a list of fields: the seller
/// cares about being able to contact the buyer, not about which channel.
/// </para>
/// </summary>
public static class BuyerFieldRules
{
    public const char AlternativeSeparator = '|';

    public static IReadOnlySet<string> Fields { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "email", "phone", "fullName", "companyName", "billingAddress", "registrationNumber",
    };

    /// <summary>
    /// Refuses an unknown field rather than ignoring it. A silently dropped rule is a checkout
    /// that stops collecting something the seller believes it is collecting.
    /// </summary>
    public static void Validate(IReadOnlyList<string> rules)
    {
        if (rules is null || rules.Count == 0)
            throw new DomainException("VALIDATION_FAILED", "At least one buyer field rule is required.");

        foreach (var rule in rules)
        {
            var alternatives = (rule ?? string.Empty).Split(AlternativeSeparator);

            if (alternatives.Any(string.IsNullOrWhiteSpace))
            {
                throw new DomainException("VALIDATION_FAILED",
                    $"'{rule}' is not a valid buyer field rule: every alternative must be a field name.");
            }

            var unknown = alternatives.Where(alternative => !Fields.Contains(alternative)).ToList();
            if (unknown.Count > 0)
            {
                throw new DomainException("VALIDATION_FAILED",
                    $"Unknown buyer field(s): {string.Join(", ", unknown)}. Known fields are {string.Join(", ", Fields)}.");
            }
        }
    }

    /// <summary>Whether the fields a buyer supplied satisfy every rule.</summary>
    public static bool IsSatisfiedBy(IReadOnlyList<string> rules, IReadOnlySet<string> supplied) =>
        rules.All(rule => rule.Split(AlternativeSeparator).Any(supplied.Contains));
}
