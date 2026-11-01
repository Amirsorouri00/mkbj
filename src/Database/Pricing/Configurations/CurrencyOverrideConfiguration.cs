using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Pricing.Configurations;

internal sealed class CurrencyOverrideConfiguration : IEntityTypeConfiguration<CurrencyOverride>
{
    public void Configure(EntityTypeBuilder<CurrencyOverride> builder)
    {
        builder.ToTable("currency_overrides");

        builder.HasKey(@override => @override.Id);
        builder.Property(@override => @override.Id)
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        builder.Property(@override => @override.VariantPriceId).HasColumnType("bigint").IsRequired();
        builder.Property(@override => @override.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(@override => @override.AmountMinor).HasColumnType("bigint").IsRequired();
        builder.Property(@override => @override.PriceList).HasMaxLength(64).IsRequired();
        builder.Property(@override => @override.UpdatedAt).IsRequired();

        // One price per currency per list. The price list is in the key so a second list can be
        // added later without a migration that touches every row.
        builder.HasIndex(@override => new
            { @override.VariantPriceId, @override.Currency, @override.PriceList })
            .IsUnique()
            .HasDatabaseName("ux_currency_overrides_price_currency_list");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_currency_overrides_positive", "amount_minor > 0"));

        builder.Ignore(@override => @override.DomainEvents);
    }
}
