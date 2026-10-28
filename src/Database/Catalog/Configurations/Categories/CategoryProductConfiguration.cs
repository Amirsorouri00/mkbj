using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations.Categories;

internal sealed class CategoryProductConfiguration : IEntityTypeConfiguration<CategoryProduct>
{
    public void Configure(EntityTypeBuilder<CategoryProduct> builder)
    {
        builder.ToTable("CategoryProduct");
        builder.HasKey(assignment => new { assignment.CategoryId, assignment.ProductId });
        builder.Property(assignment => assignment.CategoryId).HasColumnType("bigint");
        builder.Property(assignment => assignment.ProductId).HasColumnType("bigint");
        builder.Property(assignment => assignment.AssignedAt).IsRequired();
        builder.Property(assignment => assignment.AssignedByUserId);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(assignment => assignment.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(assignment => new { assignment.ProductId, assignment.CategoryId })
            .HasDatabaseName("IX_CategoryProduct_ProductId_CategoryId");
    }
}
