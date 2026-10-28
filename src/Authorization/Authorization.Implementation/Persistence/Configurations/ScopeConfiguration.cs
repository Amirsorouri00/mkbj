using AuthorizationService.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorizationService.Implementation.Persistence.Configurations;

internal sealed class ScopeConfiguration : IEntityTypeConfiguration<Scope>
{
    private static readonly DateTime SeedTimestamp = new(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Scope> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Code).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();

        builder.HasIndex(s => s.Code).IsUnique();

        builder.HasData(
            new
            {
                Id = Guid.Parse("C92E7CCA-205D-4ACD-964F-4A3362AE6EA8"),
                Code = "Sell",
                Name = "Sell",
                CreatedAtUtc = SeedTimestamp,
                UpdatedAtUtc = SeedTimestamp,
            },
            new
            {
                Id = Guid.Parse("6E82A8C8-6591-4967-9BCA-A6F2BEADFEA4"),
                Code = "Finance",
                Name = "Finance",
                CreatedAtUtc = SeedTimestamp,
                UpdatedAtUtc = SeedTimestamp,
            },
            new
            {
                Id = Guid.Parse("0F8E8424-10A1-4381-91DC-34B5A71BE06E"),
                Code = "Technology",
                Name = "Technology",
                CreatedAtUtc = SeedTimestamp,
                UpdatedAtUtc = SeedTimestamp,
            });
    }
}
