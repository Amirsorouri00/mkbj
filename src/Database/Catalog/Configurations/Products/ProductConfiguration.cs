using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations.Products;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Product", table =>
        {
            table.HasCheckConstraint("CK_Product_ReservedStock_NonNegative", "[ReservedStock] >= 0");
            table.HasCheckConstraint("CK_Product_Stock_NonNegative", "[Stock] >= 0");
        });
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Sku)
               .HasConversion(s => s.Value, v => Sku.Create(v))
               .HasMaxLength(32).IsRequired();
        builder.HasIndex("Sku").IsUnique();

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);

        builder.OwnsOne(p => p.Price, m =>
        {
            m.Property(mm => mm.Amount).HasColumnName("PriceAmount").HasPrecision(18, 2);
            m.Property(mm => mm.Currency).HasColumnName("PriceCurrency").HasMaxLength(3);
            m.WithOwner();
        });

        builder.Property(p => p.Stock).IsRequired();
        builder.Property(p => p.ReservedStock).IsRequired();
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt);

        builder.HasIndex(p => p.CreatedAt).HasDatabaseName("IX_Product_CreatedAt");

        builder.Ignore(p => p.DomainEvents);
        builder.Property(p => p.RowVersion).IsRowVersion();
    }
}
