using System.Text.RegularExpressions;

using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Customer.Application.Customers.Domain;

/// <summary>
/// A phone number in E.164, which is the only form two systems can compare.
/// <para>
/// Punctuation is stripped before validating, because "+65 9123 4567" and "+6591234567" are the
/// same number and a partner will send whichever their own system stores. What is <b>not</b>
/// guessed is the country code: a bare "91234567" is refused rather than assumed to be Singapore,
/// since the seller sells internationally and a wrong guess silently attaches a real person's
/// number in another country to this customer.
/// </para>
/// </summary>
public sealed partial class Phone : ValueObject
{
    private Phone(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? raw, out Phone? phone)
    {
        phone = null;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var normalized = Punctuation().Replace(raw, string.Empty);

        if (!E164().IsMatch(normalized))
            return false;

        phone = new Phone(normalized);
        return true;
    }

    /// <summary>For loading a value that was already validated on the way in.</summary>
    public static Phone FromStorage(string value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    /// <summary>Spaces and the separators people and systems add; not the leading '+'.</summary>
    [GeneratedRegex(@"[\s\-().]")]
    private static partial Regex Punctuation();

    /// <summary>
    /// E.164: a '+', a non-zero country digit, then 6–14 more. The leading digit cannot be zero
    /// because no country code starts with one, and the total caps at 15 digits.
    /// </summary>
    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex E164();
}
