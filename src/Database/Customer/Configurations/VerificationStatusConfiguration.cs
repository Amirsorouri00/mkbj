using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Customer.Configurations;

internal sealed class VerificationStatusConfiguration : IEntityTypeConfiguration<VerificationStatus>
{
    public void Configure(EntityTypeBuilder<VerificationStatus> builder)
    {
        builder.ToTable("VerificationStatus");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id)
            .UseIdentityColumn(1,1)
            .IsRequired();

        builder.Property(v => v.Name)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasData(
            VerificationStatus.Create( 1, "NotVerified" ),
            VerificationStatus.Create( 2, "Verified" ),
            VerificationStatus.Create( 3, "VerifiedByAgent" ),
            VerificationStatus.Create( 4, "Pending" ));
    }
}
