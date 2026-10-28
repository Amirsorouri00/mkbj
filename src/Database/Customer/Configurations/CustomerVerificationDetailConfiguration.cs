using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Customer.Configurations;

internal sealed class CustomerVerificationDetailConfiguration : IEntityTypeConfiguration<CustomerVerificationDetails>
{
    public void Configure(EntityTypeBuilder<CustomerVerificationDetails> builder)
    {
        builder.ToTable("CustomerVerificationDetail");
        builder.HasKey(d => d.Id);


        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Firstname)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.Lastname)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.Fathername)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.Gender)
            .HasColumnType("int")
            .IsRequired();

        builder.Property(d => d.Liveness)
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("sysutcdatetime()")
            .IsRequired();

        // Relationships
        builder.HasOne(d => d.Verification)
            .WithOne(v => v.Details)
            .HasForeignKey<CustomerVerificationDetails>(d => d.VerificationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
