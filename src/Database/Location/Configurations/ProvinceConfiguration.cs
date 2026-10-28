using EwaysVNext.Modules.Location.Application.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Location.Configurations;

internal sealed class ProvinceConfiguration : IEntityTypeConfiguration<Province>
{
    public void Configure(EntityTypeBuilder<Province> builder)
    {
        builder.ToTable("Province");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).UseIdentityColumn(1,1);

        builder.Property(p => p.Name)
            .HasMaxLength(100)
            .IsRequired();
        builder.HasIndex(p => p.Name).IsUnique();

        builder.Property(p => p.Code)
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(p => p.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        // Relations
        builder.HasMany(p => p.Cities)
            .WithOne(c => c.Province)
            .HasForeignKey(c => c.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(c => c.DomainEvents);
        builder.Ignore(p => p.RowVersion);
    }
}
