using AuthorizationService.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorizationService.Implementation.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    private static readonly DateTime SeedTimestamp = new(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Code).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Rank).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.UpdatedAtUtc).IsRequired();

        builder.HasIndex(r => r.Code).IsUnique();

        builder.HasData(
            new
            {
                Id = Guid.Parse("AAE2A4D1-AED0-4A94-8651-AB03F8C4F8DA"),
                Code = "User",
                Name = "User",
                Rank = 1,
                CreatedAtUtc = SeedTimestamp,
                UpdatedAtUtc = SeedTimestamp,
            },
            new
            {
                Id = Guid.Parse("9452269E-9362-41A7-904F-5F3240403F66"),
                Code = "Manager",
                Name = "Manager",
                Rank = 2,
                CreatedAtUtc = SeedTimestamp,
                UpdatedAtUtc = SeedTimestamp,
            },
            new
            {
                Id = Guid.Parse("DD981160-15D1-455C-B608-D5DC72B77629"),
                Code = "Admin",
                Name = "Admin",
                Rank = 3,
                CreatedAtUtc = SeedTimestamp,
                UpdatedAtUtc = SeedTimestamp,
            });
    }
}
