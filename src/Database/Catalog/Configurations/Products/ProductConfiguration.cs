using EwaysVNext.Modules.Catalog.Application.Brands.Domain;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations.Products;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        // Named explicitly: phase 3 reads these tables from raw SQL, which must not
        // depend on how the naming convention happens to pluralise a type name.
        builder.ToTable("products");

        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id)
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        builder.Property(product => product.BrandId).HasColumnType("bigint").IsRequired();
        builder.Property(product => product.PrimaryCategoryId).HasColumnType("bigint").IsRequired();

        builder.Property(product => product.Title).HasMaxLength(Product.TitleMaxLength).IsRequired();
        builder.Property(product => product.Slug).HasMaxLength(Product.SlugMaxLength).IsRequired();
        builder.Property(product => product.ShortDescription).HasMaxLength(Product.ShortDescriptionMaxLength);
        builder.Property(product => product.Description).HasMaxLength(Product.DescriptionMaxLength);

        // Stored by name: the value appears verbatim in phase 3's storefront SQL and in the
        // check constraint below, where a bare 2 would say nothing to anyone reading it.
        builder.Property(product => product.Status)
            .HasConversion<string>()
            .HasColumnType("text")
            .IsRequired();
        builder.Property(product => product.DeactivatedByAdmin).IsRequired();
        builder.Property(product => product.Mpn).HasMaxLength(64);
        builder.Property(product => product.MergedIntoProductId).HasColumnType("bigint");

        builder.PrimitiveCollection<IReadOnlyList<long>>("SourceRefs")
            .HasField("_sourceRefs")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("source_refs")
            .IsRequired();

        builder.Property(product => product.CreatedAt).IsRequired();
        builder.Property(product => product.UpdatedAt);

        builder.HasIndex(product => product.Slug)
            .IsUnique()
            .HasDatabaseName("ux_products_slug");

        // One part number per brand: two products under the same brand with the same MPN are the
        // same product, which is exactly the duplicate an import must not create.
        builder.HasIndex(product => new { product.BrandId, product.Mpn })
            .IsUnique()
            .HasFilter("mpn IS NOT NULL")
            .HasDatabaseName("ux_products_brand_mpn");

        builder.HasIndex(product => product.PrimaryCategoryId);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_products_status", "status IN ('Draft', 'Active', 'Deactivated')"));

        // Restrict, not Cascade: deleting a brand or a category must fail loudly rather than take
        // the catalogue with it.
        builder.HasOne<Brand>()
            .WithMany()
            .HasForeignKey(product => product.BrandId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(product => product.PrimaryCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade the other way: a variant, image or identifier has no meaning without its
        // product, and leaving orphans behind would make every dedup query lie.
        builder.HasMany(product => product.Variants)
            .WithOne()
            .HasForeignKey(variant => variant.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(product => product.Media)
            .WithOne()
            .HasForeignKey(media => media.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(product => product.Identifiers)
            .WithOne()
            .HasForeignKey(identifier => identifier.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(product => product.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(product => product.Media).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(product => product.Identifiers).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(product => product.DomainEvents);
    }
}
