using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations.Products;

internal sealed class ProductIdentifierConfiguration : IEntityTypeConfiguration<ProductIdentifier>
{
    public void Configure(EntityTypeBuilder<ProductIdentifier> builder)
    {
        builder.ToTable("product_identifiers");

        builder.HasKey(identifier => identifier.Id);
        builder.Property(identifier => identifier.Id)
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        builder.Property(identifier => identifier.ProductId).HasColumnType("bigint").IsRequired();

        // By name, because the two partial indexes below name the types in their filters and a
        // numeric filter would silently drift the first time anyone reorders the enum.
        builder.Property(identifier => identifier.Type)
            .HasConversion<string>()
            .HasColumnType("text")
            .IsRequired();

        builder.Property(identifier => identifier.Value).HasMaxLength(128).IsRequired();
        builder.Property(identifier => identifier.SourceDomain).HasMaxLength(253);

        // A GS1 code identifies the article worldwide, so one may belong to exactly one product.
        // MPN is excluded: the same part number under two brands is two different parts, which
        // ux_products_brand_mpn covers instead.
        builder.HasIndex(identifier => new { identifier.Type, identifier.Value })
            .IsUnique()
            .HasFilter("type IN ('Gtin', 'Upc', 'Ean')")
            .HasDatabaseName("ux_product_identifiers_type_value");

        // A source id is unique only within the system it came from.
        builder.HasIndex(identifier => new { identifier.SourceDomain, identifier.Value })
            .IsUnique()
            .HasFilter("type = 'SourceId'")
            .HasDatabaseName("ux_product_identifiers_source");

        builder.HasIndex(identifier => identifier.ProductId);

        builder.Ignore(identifier => identifier.DomainEvents);
    }
}
