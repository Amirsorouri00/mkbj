using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations;

internal sealed class ProductReservationConfiguration : IEntityTypeConfiguration<ProductReservation>
{
    public void Configure(EntityTypeBuilder<ProductReservation> builder)
    {
        builder.ToTable("ProductReservation");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Quantity).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.Property(x => x.IsCommitted).IsRequired();
        builder.Property(x => x.CommittedAt);

        builder.HasIndex(x => new { x.IsCommitted, x.ExpiresAt })
            .HasDatabaseName("IX_ProductReservation_IsCommitted_ExpiresAt");
        builder.HasIndex(x => new { x.IsCommitted, x.CommittedAt })
            .HasDatabaseName("IX_ProductReservation_IsCommitted_CommittedAt");

        builder.HasOne<Product>()
            .WithMany(p => p.Reservations)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
