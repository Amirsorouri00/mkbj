using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations.Categories;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Category");
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id)
            .HasColumnType("bigint")
            .ValueGeneratedNever();
        builder.Property(category => category.ParentId).HasColumnType("bigint");
        builder.Property(category => category.Name).HasMaxLength(60).IsRequired();
        builder.Property(category => category.NormalizedName).HasMaxLength(120).IsRequired();
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
            { category.ParentId, category.Status, category.DisplayOrder, category.Id })
            .HasDatabaseName("IX_Category_Hierarchy");
        builder.HasIndex(category => new { category.ParentId, category.NormalizedName })
            .HasDatabaseName("IX_Category_ParentId_NormalizedName")
            .IsUnique()
            .HasFilter("[Status] <> 2");
        builder.HasIndex(category => category.Slug)
            .HasDatabaseName("IX_Category_Slug")
            .IsUnique()
            .HasFilter("[Status] <> 2");

        builder.Ignore(category => category.DomainEvents);
        builder.Property(category => category.RowVersion).IsRowVersion();
    }
}
