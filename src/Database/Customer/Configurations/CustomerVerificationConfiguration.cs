using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Customer.Configurations;

internal sealed class CustomerVerificationConfiguration : IEntityTypeConfiguration<CustomerVerification>
{
    public void Configure(EntityTypeBuilder<CustomerVerification> builder)
    {
        builder.ToTable("CustomerVerification");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.CustomerId);
        builder.Property(v => v.NationalId)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(v => v.BirthDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(v => v.Status)
            .HasColumnType("int")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(v => v.VerificationMethod)
            .HasColumnType("int")
            .HasConversion<int>()
            .IsRequired(false);

        builder.Property(v => v.ShahkarReference)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(v => v.ExpertId)
            .IsRequired(false);

        builder.Property(v => v.NationalCardReceived)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(v => v.NationalCardReviewedAt)
            .HasColumnType("datetime2(3)")
            .IsRequired(false);

        builder.Property(v => v.VerifiedAt)
            .HasColumnType("datetime2(3)")
            .IsRequired(false);

        builder.Property(v => v.CreatedAt)
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("sysutcdatetime()")
            .IsRequired();

        builder.Property(v => v.UpdatedAt)
            .HasColumnType("datetime2(3)")
            .IsRequired(false);

        builder.Property(v => v.Reason)
            .HasMaxLength(1000)
            .IsRequired(false);

        // Relationships
        builder.HasOne(v => v.Customer)
            .WithOne(c => c.Verification)
            .HasForeignKey<CustomerVerification>(v => v.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.VerificationMethodNavigation)
            .WithMany()
            .HasForeignKey(c => c.VerificationMethod)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
