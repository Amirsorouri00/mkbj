using EwaysVNext.Jobs.Implementation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Jobs.Configurations;

internal sealed class JobRecordConfiguration : IEntityTypeConfiguration<JobRecord>
{
    public void Configure(EntityTypeBuilder<JobRecord> builder)
    {
        // Named explicitly, unlike the rest of the model: PostgresJobStore is raw SQL, so a
        // convention-derived rename would break the claim statement with no compiler warning.
        builder.ToTable("job", table =>
            table.HasCheckConstraint("ck_job_status",
                "status IN ('Queued','Running','Completed','Failed')"));

        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();

        builder.Property(job => job.Type).HasMaxLength(100).IsRequired();
        builder.Property(job => job.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(job => job.RunAt).IsRequired();
        builder.Property(job => job.Attempts).IsRequired();
        builder.Property(job => job.MaxAttempts).IsRequired();
        builder.Property(job => job.LockedBy).HasMaxLength(200);
        builder.Property(job => job.LockedUntil);
        builder.Property(job => job.LastError);
        builder.Property(job => job.DedupeKey).HasMaxLength(200);
        builder.Property(job => job.CreatedAt).IsRequired();
        builder.Property(job => job.CompletedAt);

        // Unique among QUEUED rows only: a completed job must not block the next one with the same
        // key, and a refresh requested while one is running still gets its own run.
        builder.HasIndex(job => job.DedupeKey)
            .IsUnique()
            .HasFilter("status = 'Queued'")
            .HasDatabaseName("ux_job_dedupe_key_queued");

        // The claim's selection predicate.
        builder.HasIndex(job => new { job.Status, job.RunAt });
    }
}
