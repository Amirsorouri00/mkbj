using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations.Categories;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        // Named explicitly: phase 3 reads these tables from raw SQL, which must not
        // depend on how the naming convention happens to pluralise a type name.
        builder.ToTable("categories");

        
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id)
            .HasColumnType("bigint")
            .ValueGeneratedNever();
        builder.Property(category => category.ParentId).HasColumnType("bigint");
        builder.Property(category => category.Name).HasMaxLength(60).IsRequired();
        builder.Property(category => category.NormalizedName).HasMaxLength(120).IsRequired();

        builder.Property(category => category.CanonicalKey)
            .HasMaxLength(120)
            .IsRequired();

        builder.PrimitiveCollection<IReadOnlyList<string>>("Aliases")
            .HasField("_aliases")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("aliases")
            .IsRequired();
        builder.Property(category => category.Slug)
            .HasConversion(slug => slug.Value, value => CategorySlug.Create(value))
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(category => category.Description).HasMaxLength(1000);
        builder.Property(category => category.DisplayOrder).IsRequired();
        builder.Property(category => category.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(category => category.DeletedAt);
        builder.Property(category => category.CreatedAt).IsRequired();
        builder.Property(category => category.UpdatedAt);

        builder.HasOne(category => category.Parent)
            .WithMany(category => category.Children)
            .HasForeignKey(category => category.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(category => category.Children)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(category => new
            { category.ParentId, category.Status, category.DisplayOrder, category.Id });
        // NULLS NOT DISTINCT so two root categories collide: parent_id is null for both, and the
        // default NULLS DISTINCT would treat them as different rows and let a duplicate through
        // exactly at the level where duplicates are most visible.
        builder.HasIndex(category => new { category.ParentId, category.CanonicalKey })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("status <> 2")
            .HasDatabaseName("ux_categories_parent_canonical_key");

        // GIN, because the lookup asks "which category has this alias" — a containment query over
        // the array, which a btree cannot serve.
        builder.HasIndex("Aliases")
            .HasMethod("gin")
            .HasDatabaseName("ix_categories_aliases");
        builder.HasIndex(category => category.Slug)
            .IsUnique()
            .HasFilter("status <> 2");

        builder.Ignore(category => category.DomainEvents);
    }
}
