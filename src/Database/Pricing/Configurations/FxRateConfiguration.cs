using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Pricing.Configurations;

internal sealed class FxRateConfiguration : IEntityTypeConfiguration<FxRate>
{
    public void Configure(EntityTypeBuilder<FxRate> builder)
    {
        builder.ToTable("fx_rates");

        builder.HasKey(rate => rate.Id);
        builder.Property(rate => rate.Id)
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        builder.Property(rate => rate.Base).HasColumnType("char(3)").IsRequired();
        builder.Property(rate => rate.Quote).HasColumnType("char(3)").IsRequired();

        // numeric(18,10): a rate is multiplied by an amount before anything is rounded, so
        // precision lost here is money lost at the other end.
        builder.Property(rate => rate.Rate).HasColumnType("numeric(18,10)").IsRequired();

        builder.Property(rate => rate.Provider).HasMaxLength(64).IsRequired();
        builder.Property(rate => rate.RateDate).HasColumnType("date").IsRequired();
        builder.Property(rate => rate.FetchedAt).IsRequired();

        // One row per provider per day per pair. A refresh that runs twice must not double the
        // table, and a provider that revises a published rate has to be a visible conflict
        // rather than a silent second row.
        builder.HasIndex(rate => new { rate.Base, rate.Quote, rate.Provider, rate.RateDate })
            .IsUnique()
            .HasDatabaseName("ux_fx_rates_pair_provider_date");

        // The lookup shape: the newest rate for a pair, which is all pricing ever asks for.
        builder.HasIndex(rate => new { rate.Base, rate.Quote, rate.FetchedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_fx_rates_pair_fetched_at");

        builder.ToTable(table => table.HasCheckConstraint("ck_fx_rates_positive", "rate > 0"));

        builder.Ignore(rate => rate.DomainEvents);
    }
}
