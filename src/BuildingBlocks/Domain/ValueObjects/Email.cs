using System.Globalization;
using EwaysVNext.Domain.Common;

namespace EwaysVNext.Domain.ValueObjects;

public sealed class Email : ValueObject
{
    public string Value { get; }
    private Email(string value) => Value = value;

    public static Email Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new DomainException("EMAIL_EMPTY", EmailMessages.Empty);
        var normalized = input.Trim().ToLowerInvariant();
        if (normalized.Length > 256) throw new DomainException("EMAIL_TOO_LONG", EmailMessages.TooLong);
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            throw new DomainException("EMAIL_INVALID", string.Format(CultureInfo.InvariantCulture, EmailMessages.InvalidComposite, input));
        return new Email(normalized);
    }

    public static bool TryCreate(string? input, out Email? email)
    {
        try { email = Create(input); return true; }
        catch (DomainException) { email = null; return false; }
    }

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value;
}
