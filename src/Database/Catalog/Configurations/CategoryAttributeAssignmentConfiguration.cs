using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations;

internal sealed class CategoryAttributeAssignmentConfiguration : IEntityTypeConfiguration<CategoryAttributeAssignment>
{
    public void Configure(EntityTypeBuilder<CategoryAttributeAssignment> builder)
    {
        
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.CategoryId).IsRequired();
        builder.Property(a => a.AttributeBlueprintId).IsRequired();

        builder.Property(a => a.IsRequired).IsRequired();
        builder.Property(a => a.IsFilterable).IsRequired();
        builder.Property(a => a.IsSearchable).IsRequired();
        builder.Property(a => a.IsInTitle).IsRequired();
        builder.Property(a => a.TitleOrder);

        builder.Property(a => a.IsPrimary).IsRequired();
        builder.Property(a => a.IsVariantGenerating).IsRequired();

        builder.Property(a => a.RemovedAt);
        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt);

        builder.HasIndex(a => new { a.CategoryId, a.AttributeBlueprintId })
            .IsUnique()
            .HasFilter("removed_at IS NULL");

        builder.HasIndex(a => a.CategoryId);

        builder.HasIndex(a => new { a.CategoryId, a.IsPrimary })
            .HasFilter("removed_at IS NULL AND is_primary");

        builder.Ignore(p => p.DomainEvents);
    }
}
