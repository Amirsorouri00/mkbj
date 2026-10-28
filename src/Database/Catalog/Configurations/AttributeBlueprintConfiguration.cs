using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations;

internal sealed class AttributeBlueprintConfiguration : IEntityTypeConfiguration<AttributeBlueprint>
{
    public void Configure(EntityTypeBuilder<AttributeBlueprint> builder)
    {
        builder.ToTable("AttributeBlueprints");
        builder.HasKey(blueprint => blueprint.Id);
        builder.Property(blueprint => blueprint.Id).ValueGeneratedNever();

        builder.Property(blueprint => blueprint.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(blueprint => blueprint.Name)
            .IsUnique()
            .HasFilter("[DeletedAt] IS NULL");

        builder.Property(blueprint => blueprint.DataType).IsRequired();
        builder.Property(blueprint => blueprint.Status).IsRequired();
        builder.Property(blueprint => blueprint.PreviousStatus);
        builder.Property(blueprint => blueprint.DeletedAt);
        builder.Property(blueprint => blueprint.CreatedAt).IsRequired();
        builder.Property(blueprint => blueprint.UpdatedAt);

        builder.OwnsMany(blueprint => blueprint.Options, option =>
        {
            option.ToTable("AttributeBlueprintOptions");
            option.WithOwner().HasForeignKey(value => value.AttributeBlueprintId);
            option.HasKey(value => value.Id);
            option.Property(value => value.Id).ValueGeneratedNever();
            option.Property(value => value.Value)
                .HasMaxLength(1000)
                .IsRequired();
            option.Property(value => value.MeasurementUnitId);
            option.HasIndex(value => value.AttributeBlueprintId)
                .HasDatabaseName("IX_AttributeBlueprintOptions_AttributeBlueprintId");
            option.HasIndex(value => value.MeasurementUnitId)
                .HasDatabaseName("IX_AttributeBlueprintOptions_MeasurementUnitId");
            option.HasOne<MeasurementUnit>()
                .WithMany()
                .HasForeignKey(value => value.MeasurementUnitId)
                .OnDelete(DeleteBehavior.Restrict);
            option.Ignore(value => value.DomainEvents);
            option.Property(value => value.RowVersion).IsRowVersion();
        });

        builder.HasQueryFilter(blueprint => !blueprint.DeletedAt.HasValue);
        builder.Ignore(blueprint => blueprint.DomainEvents);
        builder.Property(blueprint => blueprint.RowVersion).IsRowVersion();
    }
}
