using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using EwaysVNext.Modules.Location.Application.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Modules.Customer.Adapter.Outbound.Repositories.SqlServer.Configurations;
internal sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{    
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("CustomerAddress");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.HasOne<Address>()
            .WithMany()
            .HasForeignKey(e => e.AddressId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
