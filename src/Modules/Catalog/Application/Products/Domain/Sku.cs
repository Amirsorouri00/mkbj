using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

public sealed class Sku : ValueObject
{
    public string Value { get; }
    private Sku(string value) => Value = value;

    public static Sku Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new DomainException("SKU_EMPTY", SkuMessages.Empty);
        var normalized = input.Trim().ToUpperInvariant();
        if (normalized.Length is < 3 or > 32) throw new DomainException("SKU_LENGTH", SkuMessages.Length);
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^[A-Z0-9\-]+$"))
            throw new DomainException("SKU_FORMAT", SkuMessages.Format);
        return new Sku(normalized);
    }

    public static bool TryCreate(string? input, out Sku? sku)
    {
        try { sku = Create(input); return true; }
        catch (DomainException) { sku = null; return false; }
    }

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value;
}
