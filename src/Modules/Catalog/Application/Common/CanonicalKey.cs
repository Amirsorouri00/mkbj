using System.Globalization;
using System.Text;

using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.Common;

/// <summary>
/// Reduces a name to a stable key, so two spellings of one thing are recognised as the same
/// thing — "Café Crème", "CAFE CREME" and "cafe-creme" all arrive as <c>cafe-creme</c>.
/// <para>
/// This is what makes an import idempotent. A supplier feed re-sent tomorrow with different
/// capitalisation must update the same category rather than create a second one beside it, and
/// nothing downstream can tell those two cases apart once the rows exist.
/// </para>
/// <para>
/// It folds accents, case, punctuation and spacing, and nothing else. "Kitchen &amp; Dining" and
/// "Kitchen and Dining" stay different keys: guessing at wording would eventually merge two
/// things that were never the same, which is far harder to notice than a duplicate. Matching
/// those is what an alias is for.
/// </para>
/// </summary>
public static class CanonicalKey
{
    /// <summary>
    /// Long enough for a real category path, short enough to index. A name past this is almost
    /// always a description that arrived in the wrong field.
    /// </summary>
    public const int MaxLength = 120;

    private const char Separator = '-';

    public static string From(string input)
    {
        if (!TryNormalize(input, out var key, out var error))
            throw new DomainException(error!, Message(error!, input));

        return key;
    }

    /// <summary>For callers that treat a bad name as data rather than as a fault — an import row.</summary>
    public static bool TryFrom(string? input, out string key) =>
        TryNormalize(input, out key, out _);

    private static bool TryNormalize(string? input, out string key, out string? error)
    {
        key = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "CANONICAL_KEY_EMPTY";
            return false;
        }

        // FormKD splits an accented character into its base letter plus a combining mark, which
        // the filter below then drops — so "é" becomes "e" without a per-language lookup table.
        var decomposed = input.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
            {
                // Held back rather than written immediately, so a run of punctuation collapses to
                // one separator and a trailing run leaves none at all.
                if (pendingSeparator && builder.Length > 0)
                    builder.Append(Separator);

                pendingSeparator = false;
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            pendingSeparator = true;
        }

        if (builder.Length == 0)
        {
            error = "CANONICAL_KEY_EMPTY";
            return false;
        }

        // Back to composed form: the key is stored and compared as text, and a decomposed string
        // would not equal the same characters typed normally.
        var normalized = builder.ToString().Normalize(NormalizationForm.FormC);

        if (normalized.Length > MaxLength)
        {
            error = "CANONICAL_KEY_TOO_LONG";
            return false;
        }

        key = normalized;
        return true;
    }

    private static string Message(string error, string? input) => error switch
    {
        "CANONICAL_KEY_TOO_LONG" =>
            $"A canonical key cannot exceed {MaxLength} characters; '{input}' is longer.",
        _ => $"'{input}' contains no letters or digits, so it has no canonical key.",
    };
}
