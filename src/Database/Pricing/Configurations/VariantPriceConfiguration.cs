using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Pricing.Configurations;

internal sealed class VariantPriceConfiguration : IEntityTypeConfiguration<VariantPrice>
{
    public void Configure(EntityTypeBuilder<VariantPrice> builder)
    {
        builder.ToTable("variant_prices");

        builder.HasKey(price => price.Id);
        builder.Property(price => price.Id)
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        // Unique: one price aggregate per variant. Two would make "what does this cost" a
        // question with two answers and no rule for choosing.
        builder.Property(price => price.VariantId).HasColumnType("bigint").IsRequired();
        builder.HasIndex(price => price.VariantId)
            .IsUnique()
            .HasDatabaseName("ux_variant_prices_variant");

        builder.Property(price => price.BaseAmountMinor).HasColumnType("bigint").IsRequired();
        builder.Property(price => price.BaseCurrency).HasColumnType("char(3)").IsRequired();
        builder.Property(price => price.CreatedAt).IsRequired();
        builder.Property(price => price.UpdatedAt).IsRequired();

        // Owned columns rather than a jsonb blob: provenance is read by humans investigating a
        // price, and a column they can filter on is worth more than one they have to unpack.
        builder.OwnsOne(price => price.Provenance, provenance =>
        {
            provenance.Property(value => value.ObservationId)
                .HasColumnName("provenance_observation_id").HasColumnType("bigint");
            provenance.Property(value => value.SourceUrl)
                .HasColumnName("provenance_source_url").HasMaxLength(2048);
            provenance.Property(value => value.CollectedAt)
                .HasColumnName("provenance_collected_at");
            provenance.Property(value => value.OriginalAmount)
                .HasColumnName("provenance_original_amount").HasColumnType("numeric(18,4)");
            provenance.Property(value => value.OriginalCurrency)
                .HasColumnName("provenance_original_currency").HasMaxLength(8);
            provenance.Property(value => value.TaxIncluded)
                .HasColumnName("provenance_tax_included");
        });
        builder.Navigation(price => price.Provenance).IsRequired();

        builder.HasMany(price => price.Overrides)
            .WithOne()
            .HasForeignKey(@override => @override.VariantPriceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(price => price.Overrides).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_variant_prices_positive", "base_amount_minor > 0"));

        builder.Ignore(price => price.DomainEvents);
    }
}
