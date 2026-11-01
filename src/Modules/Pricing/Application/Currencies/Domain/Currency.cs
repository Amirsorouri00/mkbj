using System.Text.RegularExpressions;
using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Pricing.Application.Currencies.Domain;

/// <summary>
/// A currency the seller may price in.
/// </summary>
/// <remarks>
/// The primary key is a <c>long</c> and the ISO code is a unique column, not the key — rule 10,
/// and the spec's "Code (PK)" read the other way. The ids are the ISO-4217 numeric codes, so the
/// key is still the currency's own identity rather than an arbitrary sequence number.
/// </remarks>
public sealed partial class Currency : AggregateRoot<long>
{
    /// <summary>
    /// Every currency the MVP supports has two minor units, which is the assumption
    /// <c>MinorUnits</c> at the persistence edge is built on. Stored per currency anyway so the
    /// day one of them does not, the data says so rather than the code having to be found.
    /// </summary>
    public const int DefaultMinorUnits = 2;

    public string Code { get; private set; } = null!;

    public bool IsEnabled { get; private set; }

    public int MinorUnits { get; private set; } = DefaultMinorUnits;

    public DateTimeOffset? UpdatedAt { get; private set; }

    private Currency() { }

    public static Currency Create(long id, string code, bool isEnabled = true,
        int minorUnits = DefaultMinorUnits)
    {
        if (id <= 0)
        {
            throw new DomainException("CURRENCY_ID_INVALID", "Currency ID must be positive.");
        }

        return new Currency
        {
            Id = id,
            Code = NormalizeCode(code),
            IsEnabled = isEnabled,
            MinorUnits = minorUnits,
        };
    }

    public void SetEnabled(bool enabled, DateTimeOffset now)
    {
        if (IsEnabled == enabled) return;

        IsEnabled = enabled;
        UpdatedAt = now;
    }

    /// <summary>
    /// Upper-case, exactly three letters. Not a lenient parse: a currency code is the one field
    /// a wrong value in makes every amount beside it meaningless.
    /// </summary>
    public static string NormalizeCode(string code)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (!CodePattern().IsMatch(normalized))
        {
            throw new DomainException(
                "CURRENCY_CODE_INVALID", $"'{code}' is not a three-letter currency code.");
        }

        return normalized;
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CodePattern();
}
