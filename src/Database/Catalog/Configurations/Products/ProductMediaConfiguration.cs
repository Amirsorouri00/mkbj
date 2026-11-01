using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations.Products;

internal sealed class ProductMediaConfiguration : IEntityTypeConfiguration<ProductMedia>
{
    public void Configure(EntityTypeBuilder<ProductMedia> builder)
    {
        builder.ToTable("product_media");

        builder.HasKey(media => media.Id);
        builder.Property(media => media.Id)
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        builder.Property(media => media.ProductId).HasColumnType("bigint").IsRequired();
        builder.Property(media => media.ObjectKey).HasMaxLength(512).IsRequired();
        builder.Property(media => media.SortOrder).IsRequired();
        builder.Property(media => media.Alt).HasMaxLength(500);
        builder.Property(media => media.SourceUrl).HasMaxLength(2048);

        // By name, like the product's status: this column decides whether an image reaches a
        // customer, and phase 3's SQL filters on it.
        builder.Property(media => media.UsageRights)
            .HasConversion<string>()
            .HasColumnType("text")
            .IsRequired();

        builder.Property(media => media.CreatedAt).IsRequired();

        // The order a product's gallery is read in, which is the only way these rows are queried.
        builder.HasIndex(media => new { media.ProductId, media.SortOrder });

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_product_media_usage_rights",
            "usage_rights IN ('Allowed', 'RightsUnknown', 'Denied')"));

        builder.Ignore(media => media.DomainEvents);
    }
}
