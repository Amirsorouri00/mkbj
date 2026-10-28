using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;

namespace EwaysVNext.Database.Customer.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<CustomerAggregate>
{
    public void Configure(EntityTypeBuilder<CustomerAggregate> builder)
    {
        builder.ToTable("Customer");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.MobileNumber)
            .HasMaxLength(20)
            .IsRequired();
        builder.HasIndex(c => c.MobileNumber).IsUnique();

        builder.Property(c => c.FirstName)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(c => c.LastName)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(c => c.Gender)
            .HasColumnType("int")
            .IsRequired(false);

        builder.Property(c => c.NationalCode)
            .HasMaxLength(10)
            .IsRequired(false);

        builder.Property(c => c.BirthDate)
            .HasColumnType("date")
            .IsRequired(false);

        builder.Property(c => c.EconomicCode)
            .HasMaxLength(10)
            .IsRequired(false);

        builder.Property(c => c.IntaCode)
            .HasColumnName("INTACode")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(c => c.RoleCode)
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(c => c.Email)
            .HasMaxLength(256)
            .IsRequired(false);

        builder.Property(c => c.SecondaryPhoneNumber)
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(c => c.CreatedAt)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnType("date")
            .IsRequired(false);

        // Relationships
        builder.HasOne(c => c.CustomerStatusNavigation)
            .WithMany()
            .HasForeignKey(c => c.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.VerificationStatusNavigation)
            .WithMany()
            .HasForeignKey(c => c.VerificationStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.JobTypes)
            .WithOne()
            .HasForeignKey(j => j.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.JobTypes).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Ignore domain events if you have them
        builder.Ignore(c => c.DomainEvents);
        builder.Ignore(p => p.RowVersion);
    }
}
