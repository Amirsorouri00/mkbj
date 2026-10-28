using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations;

internal sealed class CategoryAttributeExclusionConfiguration : IEntityTypeConfiguration<CategoryAttributeExclusion>
{
    public void Configure(EntityTypeBuilder<CategoryAttributeExclusion> builder)
    {
        builder.ToTable("CategoryAttributeExclusions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.CategoryId).IsRequired();
        builder.Property(e => e.SourceAssignmentId).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.HasIndex(e => new { e.CategoryId, e.SourceAssignmentId })
            .IsUnique()
            .HasDatabaseName("IX_CategoryAttributeExclusions_Category_Source");

        builder.HasIndex(e => e.CategoryId).HasDatabaseName("IX_CategoryAttributeExclusions_CategoryId");

        builder.Ignore(e => e.DomainEvents);
        builder.Property(e => e.RowVersion).IsRowVersion();
    }
}
