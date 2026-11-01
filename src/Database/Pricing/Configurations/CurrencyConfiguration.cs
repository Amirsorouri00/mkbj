using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Pricing.Configurations;

internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("currencies");

        builder.HasKey(currency => currency.Id);
        builder.Property(currency => currency.Id)
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        builder.Property(currency => currency.Code)
            .HasColumnType("char(3)")
            .IsRequired();
        builder.Property(currency => currency.IsEnabled).IsRequired();
        builder.Property(currency => currency.MinorUnits).IsRequired();
        builder.Property(currency => currency.UpdatedAt);

        builder.HasIndex(currency => currency.Code)
            .IsUnique()
            .HasDatabaseName("ux_currencies_code");

        builder.Ignore(currency => currency.DomainEvents);

        // The ids are the ISO-4217 numeric codes, so the key carries the currency's own identity
        // rather than the order someone happened to insert them in.
        builder.HasData(
            Seed(840, "USD"),
            Seed(978, "EUR"),
            Seed(702, "SGD"),
            Seed(784, "AED"),
            Seed(826, "GBP"));
    }

    /// <summary>
    /// Seeded as an anonymous shape rather than through <c>Currency.Create</c>: EF's
    /// <c>HasData</c> wants the column values, and a constructed aggregate would carry a
    /// <c>RowVersion</c> the seed has no business setting.
    /// </summary>
    private static object Seed(long id, string code) => new
    {
        Id = id,
        Code = code,
        IsEnabled = true,
        MinorUnits = Currency.DefaultMinorUnits,
    };
}
