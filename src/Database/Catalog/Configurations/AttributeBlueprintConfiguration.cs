using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations;

internal sealed class AttributeBlueprintConfiguration : IEntityTypeConfiguration<AttributeBlueprint>
{
    public void Configure(EntityTypeBuilder<AttributeBlueprint> builder)
    {
        // Named explicitly: phase 3 reads these tables from raw SQL, which must not
        // depend on how the naming convention happens to pluralise a type name.
        builder.ToTable("attribute_blueprints");

        
        builder.HasKey(blueprint => blueprint.Id);
        builder.Property(blueprint => blueprint.Id).ValueGeneratedNever();

        builder.Property(blueprint => blueprint.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(blueprint => blueprint.Name)
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.Property(blueprint => blueprint.CanonicalKey)
            .HasMaxLength(120)
            .IsRequired();

        builder.PrimitiveCollection<IReadOnlyList<string>>("Aliases")
            .HasField("_aliases")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("aliases")
            .IsRequired();

        builder.Property(blueprint => blueprint.Scope)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(blueprint => blueprint.ScopeCategoryId).HasColumnType("bigint");
        builder.Property(blueprint => blueprint.UnitId).HasColumnType("bigint");

        // NULLS NOT DISTINCT so two global attributes collide: scope_category_id is null for
        // every one of them, and the default NULLS DISTINCT would read each null as its own
        // scope and let duplicate global keys through.
        builder.HasIndex(blueprint => new { blueprint.ScopeCategoryId, blueprint.CanonicalKey })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_attribute_blueprints_scope_key");

        builder.HasIndex(blueprint => blueprint.ScopeCategoryId)
            .HasFilter("scope_category_id IS NOT NULL");

        // GIN, because the lookup asks "which attribute answers to this column heading" — a
        // containment query over the array, which a btree cannot serve.
        builder.HasIndex("Aliases")
            .HasMethod("gin")
            .HasDatabaseName("ix_attribute_blueprints_aliases");

        builder.HasOne<MeasurementUnit>()
            .WithMany()
            .HasForeignKey(blueprint => blueprint.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(blueprint => blueprint.DataType).IsRequired();
        builder.Property(blueprint => blueprint.Status).IsRequired();
        builder.Property(blueprint => blueprint.PreviousStatus);
        builder.Property(blueprint => blueprint.DeletedAt);
        builder.Property(blueprint => blueprint.CreatedAt).IsRequired();
        builder.Property(blueprint => blueprint.UpdatedAt);

        builder.OwnsMany(blueprint => blueprint.Options, option =>
        {
            // Plural, matching every other table. The convention had produced the singular
            // "attribute_blueprint_option" for this owned collection, which phase 3's SQL would
            // have had to know about as a one-off.
            option.ToTable("attribute_blueprint_options");

            
            option.WithOwner().HasForeignKey(value => value.AttributeBlueprintId);
            option.HasKey(value => value.Id);
            option.Property(value => value.Id).ValueGeneratedNever();
            option.Property(value => value.Value)
                .HasMaxLength(1000)
                .IsRequired();
            option.Property(value => value.MeasurementUnitId);
            option.HasIndex(value => value.AttributeBlueprintId);
            option.HasIndex(value => value.MeasurementUnitId);
            option.HasOne<MeasurementUnit>()
                .WithMany()
                .HasForeignKey(value => value.MeasurementUnitId)
                .OnDelete(DeleteBehavior.Restrict);
            option.Ignore(value => value.DomainEvents);
        });

        builder.HasQueryFilter(blueprint => !blueprint.DeletedAt.HasValue);
        builder.Ignore(blueprint => blueprint.DomainEvents);
    }
}
