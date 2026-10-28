using EwaysVNext.Modules.Location.Application.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Location.Configurations;

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("City");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).UseIdentityColumn(1,1);

        builder.Property(c => c.ProvinceId)
            .IsRequired();

        builder.Property(c => c.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Code)
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(c => c.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        // Unique constraint: ترکیب استان و شهر یکتا باشد
        builder.HasIndex(c => new { c.ProvinceId, c.Name })
            .IsUnique();

        // Relations
        builder.HasOne(c => c.Province)
            .WithMany(p => p.Cities)
            .HasForeignKey(c => c.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Districts)
            .WithOne(d => d.City)
            .HasForeignKey(d => d.CityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
