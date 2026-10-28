using EwaysVNext.Modules.Location.Application.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Location.Configurations;

internal sealed class AddressStatusConfiguration : IEntityTypeConfiguration<AddressStatus>
{
    public void Configure(EntityTypeBuilder<AddressStatus> builder)
    {
        builder.ToTable("AddressStatus");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .UseIdentityColumn(1, 1)
            .IsRequired();

        builder.Property(a => a.Name)
            .HasMaxLength(10)
            .IsRequired();

        builder.HasData(
            AddressStatus.Create(1, "Active"),
            AddressStatus.Create(2, "Inactive"),
            AddressStatus.Create(3, "Deleted"),
            AddressStatus.Create(4, "Archived")
            );
    }
}
