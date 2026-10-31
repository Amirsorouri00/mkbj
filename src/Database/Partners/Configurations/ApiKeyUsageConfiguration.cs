using EwaysVNext.Modules.Partners.Application.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Partners.Configurations;

/// <summary>
/// Append-only request history. No foreign key to <c>partners</c> on purpose: these rows outlive
/// the aggregate's shape and must never be the reason a partner cannot be removed, nor cascade
/// away as evidence when one is.
/// </summary>
internal sealed class ApiKeyUsageConfiguration : IEntityTypeConfiguration<ApiKeyUsage>
{
    public void Configure(EntityTypeBuilder<ApiKeyUsage> builder)
    {
        builder.HasKey(usage => usage.Id);
        builder.Property(usage => usage.Id).ValueGeneratedNever();

        builder.Property(usage => usage.PartnerId).IsRequired();
        builder.Property(usage => usage.KeyId).IsRequired();
        builder.Property(usage => usage.At).IsRequired();

        builder.Property(usage => usage.Method).HasMaxLength(16).IsRequired();
        builder.Property(usage => usage.Path).HasMaxLength(512).IsRequired();
        builder.Property(usage => usage.Status).IsRequired();
        builder.Property(usage => usage.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(usage => usage.DurationMs).IsRequired();

        // The prune job deletes by age across every partner, so it needs `at` alone.
        builder.HasIndex(usage => usage.At);

        // The admin read is always "this partner, most recent first", which this serves as a
        // single range scan. The column order matters: (at, partner_id) could not.
        builder.HasIndex(usage => new { usage.PartnerId, usage.At })
            .IsDescending(false, true);

        builder.ToTable("api_key_usage");
    }
}
