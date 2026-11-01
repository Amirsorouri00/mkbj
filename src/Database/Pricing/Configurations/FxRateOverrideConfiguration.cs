using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Pricing.Configurations;

internal sealed class FxRateOverrideConfiguration : IEntityTypeConfiguration<FxRateOverride>
{
    public void Configure(EntityTypeBuilder<FxRateOverride> builder)
    {
        builder.ToTable("fx_rate_overrides");

        builder.HasKey(@override => @override.Id);
        builder.Property(@override => @override.Id)
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        builder.Property(@override => @override.Base).HasColumnType("char(3)").IsRequired();
        builder.Property(@override => @override.Quote).HasColumnType("char(3)").IsRequired();
        builder.Property(@override => @override.Rate).HasColumnType("numeric(18,10)").IsRequired();
        builder.Property(@override => @override.SetBy).IsRequired();
        builder.Property(@override => @override.SetAt).IsRequired();
        builder.Property(@override => @override.ValidUntil);
        builder.Property(@override => @override.IsActive).IsRequired();

        // Unfiltered, so there is exactly one row per pair whether it is active or not. A
        // filtered index would let a withdrawn override sit beside a live one, and "which
        // override applies" would stop having one answer.
        builder.HasIndex(@override => new { @override.Base, @override.Quote })
            .IsUnique()
            .HasDatabaseName("ux_fx_rate_overrides_pair");

        builder.ToTable(table => table.HasCheckConstraint("ck_fx_rate_overrides_positive", "rate > 0"));

        builder.Ignore(@override => @override.DomainEvents);
    }
}
