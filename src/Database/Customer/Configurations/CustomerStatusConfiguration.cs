using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Customer.Configurations;

internal sealed class CustomerStatusConfiguration : IEntityTypeConfiguration<CustomerStatus>
{
    public void Configure(EntityTypeBuilder<CustomerStatus> builder)
    {
        builder.ToTable("CustomerStatus");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .UseIdentityColumn(1,1)
            .IsRequired();

        builder.Property(a => a.Name)
            .HasMaxLength(20)
            .IsRequired();

        builder.HasData(
            CustomerStatus.Create(1, "Active"),
            CustomerStatus.Create(2, "Inactive"),
            CustomerStatus.Create(3, "Suspended"),
            CustomerStatus.Create(4, "PendingApproval"),
            CustomerStatus.Create(5, "Approved"),
            CustomerStatus.Create(6, "Rejected"));
    }
}
