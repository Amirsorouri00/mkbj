using EwaysVNext.Modules.Location.Application.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Location.Configurations;

internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("Address");
        builder.HasKey(e => e.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(a => a.DistrictId).IsRequired(false);

        builder.Property(a => a.AddressText)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.Property(a => a.PostalCode)
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(a => a.RecipientName)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(a => a.RecipientMobile)
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(a => a.IsPrimary)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(a => a.StatusId)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(a => a.CreatedAt)
            .HasDefaultValueSql("getdate()")
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .IsRequired(false);

        builder.HasOne(c=>c.AddressStatusNavigation)
            .WithMany()
            .HasForeignKey(c => c.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(c => c.DomainEvents);
        builder.Ignore(p => p.RowVersion);
    }
}
