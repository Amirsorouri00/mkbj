using EwaysVNext.Modules.Catalog.Application.Brands.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Catalog.Configurations;

internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        // Named explicitly: phase 3 reads these tables from raw SQL, which must not
        // depend on how the naming convention happens to pluralise a type name.
        builder.ToTable("brands");

        builder.HasKey(brand => brand.Id);
        builder.Property(brand => brand.Id)
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        builder.Property(brand => brand.CanonicalName)
            .HasMaxLength(120)
            .IsRequired();
        builder.Property(brand => brand.DisplayName)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(brand => brand.Provenance)
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(brand => brand.IsActive).IsRequired();
        builder.Property(brand => brand.CreatedAt).IsRequired();
        builder.Property(brand => brand.UpdatedAt);

        builder.HasIndex(brand => brand.CanonicalName)
            .IsUnique()
            .HasDatabaseName("ux_brands_canonical_name");

        // The set is closed in the aggregate; repeated here because an import's fix-up script
        // writes these rows directly and never passes through it.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_brands_provenance", "provenance IN ('admin', 'import', 'seed')"));

        builder.OwnsMany(brand => brand.Aliases, alias =>
        {
            alias.ToTable("brand_aliases");

            alias.WithOwner().HasForeignKey(value => value.BrandId);
            alias.HasKey(value => value.Id);
            alias.Property(value => value.Id)
                .HasColumnType("bigint")
                .ValueGeneratedNever();
            alias.Property(value => value.Alias)
                .HasMaxLength(120)
                .IsRequired();

            // Unique across every brand, not per brand: an alias exists to answer "which brand is
            // this", and two brands claiming one spelling makes that question unanswerable.
            alias.HasIndex(value => value.Alias)
                .IsUnique()
                .HasDatabaseName("ux_brand_aliases_alias");
            alias.HasIndex(value => value.BrandId);
            alias.Ignore(value => value.DomainEvents);
        });

        builder.Navigation(brand => brand.Aliases)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(brand => brand.DomainEvents);
    }
}
