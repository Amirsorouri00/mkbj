using System.Globalization;
using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.Products.Domain;

/// <summary>
/// One code a product is known by, in one namespace.
/// <para>
/// Identifiers are how the same product arriving from two feeds is recognised as one product, so
/// a value accepted in a shape nobody else writes it in is worse than one refused: it will match
/// nothing, quietly, forever. Everything that reaches the database goes through
/// <see cref="Normalize"/>, including the matching code, which calls it without constructing an
/// entity so that "does this feed row match?" and "what is stored?" cannot drift apart.
/// </para>
/// </summary>
public sealed class ProductIdentifier : Entity<long>
{
    private const int MpnMaxLength = 64;
    private const int SourceIdMaxLength = 128;
    private const int SourceDomainMaxLength = 253;

    public long ProductId { get; private set; }

    public ProductIdentifierType Type { get; private set; }

    public string Value { get; private set; } = null!;

    /// <summary>
    /// The host the identifier belongs to, for <see cref="ProductIdentifierType.SourceId"/> only.
    /// A GTIN means the same thing wherever it came from; a source id means nothing without it.
    /// </summary>
    public string? SourceDomain { get; private set; }

    private ProductIdentifier() { }

    internal static ProductIdentifier Create(long id, long productId, ProductIdentifierType type,
        string value, string? sourceDomain)
    {
        if (id <= 0)
        {
            throw new DomainException("IDENTIFIER_ID_INVALID", "Identifier ID must be positive.");
        }

        if (productId <= 0)
        {
            throw new DomainException("IDENTIFIER_PRODUCT_ID_INVALID", "Product ID must be positive.");
        }

        return new ProductIdentifier
        {
            Id = id,
            ProductId = productId,
            Type = type,
            Value = Normalize(type, value, sourceDomain),
            SourceDomain = NormalizeDomain(type, sourceDomain)
        };
    }

    /// <summary>
    /// The stored form of a raw value, or a <see cref="DomainException"/> saying why there is
    /// none. Static so matching can ask the same question without an entity.
    /// </summary>
    public static string Normalize(ProductIdentifierType type, string value, string? sourceDomain)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException("IDENTIFIER_TYPE_INVALID", "Identifier type is invalid.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("IDENTIFIER_VALUE_REQUIRED", "Identifier value is required.");
        }

        _ = NormalizeDomain(type, sourceDomain);

        return type switch
        {
            ProductIdentifierType.Gtin => NormalizeGs1(value, [8, 12, 13, 14]),
            ProductIdentifierType.Upc => NormalizeGs1(value, [12]),
            ProductIdentifierType.Ean => NormalizeGs1(value, [8, 13]),
            ProductIdentifierType.Mpn => NormalizeMpn(value),
            _ => NormalizeSourceId(value)
        };
    }

    private static string NormalizeGs1(string value, int[] allowedLengths)
    {
        // Suppliers print these with spaces and dashes at the group boundaries; the code is the
        // digits. MPN keeps its punctuation because there the dash is part of the part number.
        var digits = value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            throw new DomainException(
                "IDENTIFIER_FORMAT_INVALID", "A GS1 identifier must contain digits only.");
        }

        if (!allowedLengths.Contains(digits.Length))
        {
            throw new DomainException(
                "IDENTIFIER_LENGTH_INVALID",
                $"This identifier must be {string.Join(" or ", allowedLengths)} digits long; {digits.Length} were given.");
        }

        if (!Gs1.HasValidCheckDigit(digits))
        {
            throw new DomainException(
                "IDENTIFIER_CHECK_DIGIT_INVALID", "The identifier's check digit does not match.");
        }

        return digits;
    }

    private static string NormalizeMpn(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length > MpnMaxLength)
        {
            throw new DomainException(
                "IDENTIFIER_LENGTH_INVALID", $"An MPN cannot exceed {MpnMaxLength} characters.");
        }

        return normalized;
    }

    private static string NormalizeSourceId(string value)
    {
        // Trimmed but not re-cased: this is the other system's key, and changing its case would
        // stop it matching the thing it was copied from.
        var normalized = value.Trim();

        if (normalized.Length > SourceIdMaxLength)
        {
            throw new DomainException(
                "IDENTIFIER_LENGTH_INVALID", $"A source ID cannot exceed {SourceIdMaxLength} characters.");
        }

        return normalized;
    }

    private static string? NormalizeDomain(ProductIdentifierType type, string? sourceDomain)
    {
        var trimmed = sourceDomain?.Trim();

        if (type != ProductIdentifierType.SourceId)
        {
            if (!string.IsNullOrEmpty(trimmed))
            {
                throw new DomainException(
                    "IDENTIFIER_SOURCE_DOMAIN_NOT_ALLOWED",
                    "Only a source ID carries a domain; every other identifier means the same thing everywhere.");
            }

            return null;
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException(
                "IDENTIFIER_SOURCE_DOMAIN_REQUIRED", "A source ID requires the domain it came from.");
        }

        if (trimmed.Length > SourceDomainMaxLength)
        {
            throw new DomainException(
                "IDENTIFIER_LENGTH_INVALID", $"A source domain cannot exceed {SourceDomainMaxLength} characters.");
        }

        // A host is case-insensitive, so storing two casings would make one source look like two.
        return trimmed.ToLower(CultureInfo.InvariantCulture);
    }
}
