using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations;

internal sealed class MeasurementUnitConfiguration : IEntityTypeConfiguration<MeasurementUnit>
{
    public void Configure(EntityTypeBuilder<MeasurementUnit> builder)
    {
        
        builder.HasKey(unit => unit.Id);
        builder.Property(unit => unit.Id).ValueGeneratedNever();
        builder.Property(unit => unit.Name)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(unit => unit.Symbol)
            .HasMaxLength(20)
            .IsRequired();
        builder.HasIndex(unit => unit.Name).IsUnique();
        builder.HasIndex(unit => unit.Symbol).IsUnique();
        builder.Ignore(unit => unit.DomainEvents);
    }
}
