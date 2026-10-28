using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using EwaysVNext.Modules.Customer.Application.Domain.TeamMembers;
using EwaysVNext.Modules.Location.Application.Domain.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Customer.Configurations;

internal sealed class LegalInformationConfiguration : IEntityTypeConfiguration<LegalInformation>
{
    public void Configure(EntityTypeBuilder<LegalInformation> builder)
    {
        builder.ToTable("LegalInformation");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.CustomerId).IsRequired();

        builder.Property(l => l.CompanyName).HasMaxLength(300).IsRequired();
        builder.Property(l => l.NationalId).HasMaxLength(11).IsRequired();
        builder.Property(l => l.RegistrationDate).HasColumnType("date").IsRequired();
        builder.Property(l => l.RegistrationNumber).HasMaxLength(50).IsRequired();
        builder.Property(l => l.CeoName).HasMaxLength(200).IsRequired();
        builder.Property(l => l.CeoMobileNumber).HasMaxLength(20).IsRequired();
        builder.Property(l => l.LandlineNumber).HasMaxLength(11).IsRequired();
        builder.Property(l => l.Fax).HasMaxLength(20).IsRequired(false);

        builder.Property(l => l.StatusId).HasColumnType("int").IsRequired();
        builder.Property(l => l.RejectionReason).HasMaxLength(1000).IsRequired(false);
        builder.Property(l => l.EditableFieldsMask).IsRequired(false);
        builder.Property(l => l.ReviewerId).IsRequired(false);
        builder.Property(l => l.SubmittedAt).HasColumnType("datetime2(3)").IsRequired(false);
        builder.Property(l => l.DecidedAt).HasColumnType("datetime2(3)").IsRequired(false);
        builder.Property(l => l.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnType("datetime2(3)").IsRequired(false);

        // Relationships - one legal-information row per customer, mutated through its lifecycle
        // (never a second, competing "active" submission).
        builder.HasOne(l => l.Customer)
            .WithOne(c => c.LegalInformation)
            .HasForeignKey<LegalInformation>(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TeamMember>().WithMany().HasForeignKey(l => l.ReviewerId).OnDelete(DeleteBehavior.Restrict);
    }
}
