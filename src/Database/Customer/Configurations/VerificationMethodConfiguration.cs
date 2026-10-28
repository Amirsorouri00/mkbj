using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Customer.Configurations;

internal sealed class VerificationMethodConfiguration : IEntityTypeConfiguration<VerificationMethod>
{
    public void Configure(EntityTypeBuilder<VerificationMethod> builder)
    {
        builder.ToTable("VerificationMethod");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .UseIdentityColumn(1, 1)
            .IsRequired();

        builder.Property(r => r.Name)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasData(
            VerificationMethod.Create(1, "None"),
            VerificationMethod.Create(2, "Shahkar"),
            VerificationMethod.Create(3, "Agent")
            );
    }
}
