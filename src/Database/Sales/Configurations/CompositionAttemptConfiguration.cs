using EwaysVNext.Modules.Sales.Application.Domain.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Sales.Configurations;

/// <summary>
/// One row per composition run. Append-only, and the only trace a failed composition leaves.
/// </summary>
internal sealed class CompositionAttemptConfiguration : IEntityTypeConfiguration<CompositionAttempt>
{
    public void Configure(EntityTypeBuilder<CompositionAttempt> builder)
    {
        builder.ToTable("composition_attempts");

        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Id).HasColumnType("bigint").ValueGeneratedNever();

        builder.Property(attempt => attempt.At).IsRequired();
        builder.Property(attempt => attempt.PartnerId).HasColumnType("bigint");
        builder.Property(attempt => attempt.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(attempt => attempt.TargetMinor).HasColumnType("bigint").IsRequired();
        builder.Property(attempt => attempt.Succeeded).IsRequired();
        builder.Property(attempt => attempt.ErrorCode).HasMaxLength(64);
        builder.Property(attempt => attempt.DurationMs).HasColumnType("bigint").IsRequired();
        builder.Property(attempt => attempt.Lines).IsRequired();
        builder.Property(attempt => attempt.Iterations).IsRequired();
        builder.Property(attempt => attempt.CandidatePoolSize).IsRequired();
        builder.Property(attempt => attempt.Seed).HasColumnType("bigint").IsRequired();
        builder.Property(attempt => attempt.StrategyVersion).HasMaxLength(16).IsRequired();
        builder.Property(attempt => attempt.Recompositions).IsRequired();
        builder.Property(attempt => attempt.OrderId).HasColumnType("bigint");

        builder.Ignore(attempt => attempt.DomainEvents);

        // Two indexes on one column, so each needs its own model-level NAME (the string
        // overload) as well as its database name. Two HasIndex(x => x.At) calls configure the
        // SAME index — the second silently renames and filters the first — and the name overload
        // alone leaves EF to invent the database names, which it does as "…_at" and "…_at1".
        builder.HasIndex([nameof(CompositionAttempt.At)], "ix_composition_attempts_at")
            .IsDescending()
            .HasDatabaseName("ix_composition_attempts_at");

        // The dashboard's "what is failing" query reads only these, and they are the minority of
        // rows — so a partial index keeps it small whatever the success rate.
        builder.HasIndex([nameof(CompositionAttempt.At)], "ix_composition_attempts_failures")
            .IsDescending()
            .HasFilter("NOT succeeded")
            .HasDatabaseName("ix_composition_attempts_failures");
    }
}
