using System.Text.Json;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations.Products;

internal sealed class VariantConfiguration : IEntityTypeConfiguration<Variant>
{
    /// <summary>
    /// Serialized with the default, deliberately boring options: this column is read by phase 3's
    /// raw SQL (<c>attribute_values-&gt;&gt;'size'</c>), so anything that renamed or re-cased the
    /// keys would break a query the C# model cannot see.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<Variant> builder)
    {
        builder.ToTable("variants");

        builder.HasKey(variant => variant.Id);
        builder.Property(variant => variant.Id)
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        builder.Property(variant => variant.ProductId).HasColumnType("bigint").IsRequired();

        builder.Property(variant => variant.Sku)
            .HasConversion(sku => sku.Value, value => Sku.Create(value))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(variant => variant.AttributeSignature)
            .HasColumnType("text")
            .IsRequired();

        // char(64), because a lower-hex SHA-256 is exactly that and a varchar would invite a
        // truncated or padded value to compare unequal to itself.
        builder.Property(variant => variant.AttributeSignatureHash)
            .HasColumnType("char(64)")
            .IsRequired();

        builder.Property(variant => variant.AttributeValues)
            .HasConversion(
                values => JsonSerializer.Serialize(values, Json),
                json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, Json)!,
                new ValueComparer<IReadOnlyDictionary<string, string>>(
                    (left, right) => Equal(left, right),
                    values => values.Count,
                    values => new Dictionary<string, string>(values)))
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(variant => variant.IsActive).IsRequired();
        builder.Property(variant => variant.DeactivatedByAdmin).IsRequired();
        builder.Property(variant => variant.CreatedAt).IsRequired();
        builder.Property(variant => variant.UpdatedAt);

        // Global, not per product: a SKU is what a picker reads off a shelf, and two of them
        // meaning different things is a warehouse problem no code can fix.
        builder.HasIndex(variant => variant.Sku)
            .IsUnique()
            .HasDatabaseName("ux_variants_sku");

        builder.HasIndex(variant => new { variant.ProductId, variant.AttributeSignatureHash })
            .IsUnique()
            .HasDatabaseName("ux_variants_product_signature");

        builder.Ignore(variant => variant.DomainEvents);
    }

    /// <summary>
    /// Compared by content rather than by reference, so EF notices an attribute change. A
    /// dictionary's own Equals is reference equality, which would make every edit invisible.
    /// </summary>
    private static bool Equal(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left.Count != right.Count) return false;

        return left.All(pair =>
            right.TryGetValue(pair.Key, out var value) &&
            string.Equals(value, pair.Value, StringComparison.Ordinal));
    }
}
