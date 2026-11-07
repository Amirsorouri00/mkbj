using EwaysVNext.Modules.Sales.Application.Domain.Carts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Sales.Configurations;

/// <summary>
/// One cart per customer, with its lines.
/// </summary>
/// <remarks>
/// The quantity range and the one-line-per-variant rule are repeated as database constraints
/// although the aggregate enforces both. A cart is the cheapest thing in the system to get wrong
/// from a stray UPDATE, and a line of 10,000 units is a charge nobody meant to make.
/// </remarks>
internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts");

        builder.HasKey(cart => cart.Id);
        builder.Property(cart => cart.Id).HasColumnType("bigint").ValueGeneratedNever();

        builder.Property(cart => cart.CustomerId).HasColumnType("bigint").IsRequired();
        builder.Property(cart => cart.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(cart => cart.UpdatedAt).IsRequired();

        builder.Ignore(cart => cart.TotalMinor);
        builder.Ignore(cart => cart.DomainEvents);

        // One cart per customer, so "the cart" is a phrase with one meaning.
        builder.HasIndex(cart => cart.CustomerId)
            .IsUnique()
            .HasDatabaseName("ux_carts_customer");

        builder.OwnsMany(cart => cart.Lines, line =>
        {
            line.ToTable("cart_lines", table => table.HasCheckConstraint(
                "ck_cart_lines_quantity", "quantity BETWEEN 1 AND 999"));

            line.WithOwner().HasForeignKey("cart_id");
            line.HasKey(l => l.Id);
            line.Property(l => l.Id).HasColumnType("bigint").ValueGeneratedNever();

            line.Property(l => l.VariantId).HasColumnType("bigint").IsRequired();
            line.Property(l => l.Quantity).IsRequired();
            line.Property(l => l.DisplayedUnitPriceMinor).HasColumnType("bigint").IsRequired();
            line.Property(l => l.PriceQuotedAt).IsRequired();

            line.Ignore(l => l.LineTotalMinor);

            // A variant appears once. Two lines for one product would let a quantity ceiling be
            // walked around by adding the same thing twice.
            line.HasIndex("cart_id", nameof(CartLine.VariantId))
                .IsUnique()
                .HasDatabaseName("ux_cart_lines_cart_variant");
        });
    }
}
