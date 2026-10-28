using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Customer.Configurations;

internal sealed class CustomerJobTypeConfiguration : IEntityTypeConfiguration<CustomerJobType>
{
    public void Configure(EntityTypeBuilder<CustomerJobType> builder)
    {
        builder.ToTable("CustomerJobType");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.HasOne<JobType>()
            .WithMany()
            .HasForeignKey(e => e.JobTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.CustomerId, e.JobTypeId }).IsUnique();
    }
}
