using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamMemberAggregate = EwaysVNext.Modules.Customer.Application.Domain.TeamMembers.TeamMember;

namespace EwaysVNext.Database.Customer.Configurations;

internal sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMemberAggregate>
{
    public void Configure(EntityTypeBuilder<TeamMemberAggregate> builder)
    {
        builder.ToTable("TeamMember");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.IdentityUserId)
            .IsRequired();

        builder.Property(t => t.FirstName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.LastName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.Gender)
            .HasColumnType("int")
            .IsRequired();

        builder.Property(t => t.NationalCode)
            .HasMaxLength(10)
            .IsRequired();
        builder.HasIndex(t => t.NationalCode).IsUnique();

        builder.Property(t => t.DateOfBirth)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(t => t.MobileNumber)
            .HasMaxLength(20)
            .IsRequired();
        builder.HasIndex(t => t.MobileNumber).IsUnique();

        builder.Property(t => t.PersonnelCode)
            .HasMaxLength(50)
            .IsRequired();
        builder.HasIndex(t => t.PersonnelCode).IsUnique();

        builder.Property(t => t.Department)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.JobTitle)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.Status)
            .HasColumnType("int")
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .IsRequired(false);

        builder.Ignore(t => t.DomainEvents);
        builder.Ignore(t => t.RowVersion);
    }
}
