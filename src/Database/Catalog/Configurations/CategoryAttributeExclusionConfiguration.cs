using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations;

internal sealed class CategoryAttributeExclusionConfiguration : IEntityTypeConfiguration<CategoryAttributeExclusion>
{
    public void Configure(EntityTypeBuilder<CategoryAttributeExclusion> builder)
    {
        // Named explicitly: phase 3 reads these tables from raw SQL, which must not
        // depend on how the naming convention happens to pluralise a type name.
        builder.ToTable("category_attribute_exclusions");

        
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.CategoryId).IsRequired();
        builder.Property(e => e.SourceAssignmentId).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.HasIndex(e => new { e.CategoryId, e.SourceAssignmentId })
            .IsUnique();

        builder.HasIndex(e => e.CategoryId);

        builder.Ignore(e => e.DomainEvents);
    }
}
