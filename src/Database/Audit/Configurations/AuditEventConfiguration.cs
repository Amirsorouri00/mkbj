using EwaysVNext.Audit.Implementation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Audit.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.ActorUserId).IsRequired();
        builder.Property(e => e.ActorRole).HasMaxLength(32).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(100).IsRequired();
        builder.Property(e => e.TargetType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.TargetId).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Before).HasColumnType("jsonb");
        builder.Property(e => e.After).HasColumnType("jsonb");
        builder.Property(e => e.CorrelationId).HasMaxLength(128).IsRequired();

        // "What happened to this thing?" and "what did this person do?" are the two questions an
        // audit log is read with.
        builder.HasIndex(e => new { e.TargetType, e.TargetId, e.OccurredAt });
        builder.HasIndex(e => new { e.ActorUserId, e.OccurredAt });
    }
}
