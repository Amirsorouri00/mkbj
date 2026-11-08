using System.Text.Json;
using EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourceListings;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Sourcing.Configurations;

/// <summary>How the import side is stored.</summary>
internal static class SourcingJson
{
    /// <summary>
    /// One serializer for every jsonb column here.
    /// </summary>
    /// <remarks>
    /// Shared so a column written by the pipeline and read by the review queue cannot disagree
    /// about casing. The documents are schema.org-shaped and stored as found, so no naming
    /// policy is applied — renaming a source's own keys would make the stored provenance
    /// pointers aim at nothing.
    /// </remarks>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.General);
}

internal sealed class SourcePolicyConfiguration : IEntityTypeConfiguration<SourcePolicy>
{
    public void Configure(EntityTypeBuilder<SourcePolicy> builder)
    {
        builder.ToTable("source_policies");

        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Id).HasColumnType("bigint").ValueGeneratedNever();

        builder.Property(policy => policy.Domain).HasMaxLength(255).IsRequired();
        builder.Property(policy => policy.Notes).HasMaxLength(2_000);
        builder.Property(policy => policy.CreatedAt).IsRequired();

        builder.Ignore(policy => policy.DomainEvents);

        // One decision per domain. Two rows for one supplier would mean the answer to "may we
        // use this?" depends on which row is read first.
        builder.HasIndex(policy => policy.Domain)
            .IsUnique()
            .HasDatabaseName("ux_source_policies_domain");
    }
}

internal sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.ToTable("import_jobs", table => table.HasCheckConstraint(
            "ck_import_jobs_limits",
            "max_pages BETWEEN 1 AND 5000 AND max_depth BETWEEN 0 AND 5 "
            + "AND max_records BETWEEN 1 AND 10000"));

        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).HasColumnType("bigint").ValueGeneratedNever();

        // Stored by name so a status is readable in a query written during an incident.
        builder.Property(job => job.Mode).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(job => job.PublishPolicy).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(job => job.FileFormat).HasConversion<string>().HasMaxLength(16);

        builder.Property(job => job.Sources).HasColumnType("text[]").IsRequired();
        builder.Property(job => job.Queries).HasColumnType("text[]").IsRequired();
        builder.Property(job => job.IncludeDomains).HasColumnType("text[]").IsRequired();
        builder.Property(job => job.ExcludeDomains).HasColumnType("text[]").IsRequired();

        builder.Property(job => job.TargetCategoryId).HasColumnType("bigint");
        builder.Property(job => job.FileRightsAttested).IsRequired();
        builder.Property(job => job.FileObjectKey).HasMaxLength(512);

        builder.ComplexProperty(job => job.Limits, limits =>
        {
            limits.Property(value => value.MaxPages).HasColumnName("max_pages").IsRequired();
            limits.Property(value => value.MaxDepth).HasColumnName("max_depth").IsRequired();
            limits.Property(value => value.MaxRecords).HasColumnName("max_records").IsRequired();
        });

        builder.ComplexProperty(job => job.Counters, counters =>
        {
            counters.Property(value => value.Discovered).HasColumnName("count_discovered").IsRequired();
            counters.Property(value => value.Extracted).HasColumnName("count_extracted").IsRequired();
            counters.Property(value => value.Valid).HasColumnName("count_valid").IsRequired();
            counters.Property(value => value.Invalid).HasColumnName("count_invalid").IsRequired();
            counters.Property(value => value.Duplicate).HasColumnName("count_duplicate").IsRequired();
            counters.Property(value => value.Published).HasColumnName("count_published").IsRequired();
            counters.Property(value => value.Review).HasColumnName("count_review").IsRequired();
        });

        builder.ComplexProperty(job => job.Checkpoint, checkpoint =>
        {
            checkpoint.Property(value => value.Phase)
                .HasColumnName("checkpoint_phase").HasConversion<string>().HasMaxLength(16).IsRequired();
            checkpoint.Property(value => value.AdapterCursor)
                .HasColumnName("checkpoint_cursor").HasMaxLength(2_048);
            checkpoint.Property(value => value.ProcessedThroughRecordId)
                .HasColumnName("checkpoint_through_id").HasColumnType("bigint").IsRequired();
        });

        builder.Property(job => job.Mapping)
            .HasConversion(
                mapping => mapping == null ? null : JsonSerializer.Serialize(mapping, SourcingJson.Options),
                json => json == null ? null : JsonSerializer.Deserialize<ImportColumnMapping>(json, SourcingJson.Options))
            .HasColumnType("jsonb");

        builder.Property(job => job.StepSequence).HasColumnType("bigint").IsRequired();
        builder.Property(job => job.ErrorCode).HasMaxLength(64);
        builder.Property(job => job.ErrorMessage).HasMaxLength(2_000);
        builder.Property(job => job.CreatedBy).IsRequired();
        builder.Property(job => job.CreatedAt).IsRequired();

        builder.Ignore(job => job.AcceptsWork);
        builder.Ignore(job => job.IsTerminal);
        builder.Ignore(job => job.DomainEvents);

        // The worker's question: what is there to pick up?
        builder.HasIndex(job => new { job.Status, job.Id })
            .HasDatabaseName("ix_import_jobs_status");
    }
}

internal sealed class StagedRecordConfiguration : IEntityTypeConfiguration<StagedRecord>
{
    public void Configure(EntityTypeBuilder<StagedRecord> builder)
    {
        builder.ToTable("staged_records");

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnType("bigint").ValueGeneratedNever();

        builder.Property(record => record.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(record => record.FirstJobId).HasColumnType("bigint").IsRequired();
        builder.Property(record => record.LastJobId).HasColumnType("bigint").IsRequired();
        builder.Property(record => record.SourceUrl).HasMaxLength(2_048).IsRequired();
        builder.Property(record => record.SourceDomain).HasMaxLength(255);

        builder.Property(record => record.RawJson).HasColumnType("jsonb").IsRequired();
        builder.Property(record => record.RawHash).HasMaxLength(64).IsRequired();
        builder.Property(record => record.NormalizedJson).HasColumnType("jsonb");

        builder.Property(record => record.ImageKeys).HasColumnType("text[]").IsRequired();
        builder.Property(record => record.PublishedVariantIds).HasColumnType("bigint[]").IsRequired();

        builder.Property(record => record.Match)
            .HasConversion(
                match => JsonSerializer.Serialize(match, SourcingJson.Options),
                json => JsonSerializer.Deserialize<MatchResult>(json, SourcingJson.Options)!)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property<List<ValidationIssue>>("_issues")
            .HasField("_issues")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("issues")
            .HasConversion(
                issues => JsonSerializer.Serialize(issues, SourcingJson.Options),
                json => JsonSerializer.Deserialize<List<ValidationIssue>>(json, SourcingJson.Options)!)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Ignore(record => record.Issues);

        builder.Property(record => record.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(record => record.ReviewNote).HasMaxLength(2_000);
        builder.Property(record => record.CategoryOverrideId).HasColumnType("bigint");
        builder.Property(record => record.PublishedProductId).HasColumnType("bigint");
        builder.Property(record => record.SourceListingId).HasColumnType("bigint");
        builder.Property(record => record.PublishSkippedDeactivated).IsRequired();
        builder.Property(record => record.CreatedAt).IsRequired();

        builder.Ignore(record => record.DomainEvents);

        // The rule the whole module turns on: one listing, one record, however many jobs find it.
        builder.HasIndex(record => record.Fingerprint)
            .IsUnique()
            .HasDatabaseName("ux_staged_records_fingerprint");

        // The review queue's own query, and the pipeline's "what is left in this job?".
        builder.HasIndex(record => new { record.LastJobId, record.Status, record.Id })
            .HasDatabaseName("ix_staged_records_job_status");
    }
}

internal sealed class SourceListingConfiguration : IEntityTypeConfiguration<SourceListing>
{
    public void Configure(EntityTypeBuilder<SourceListing> builder)
    {
        builder.ToTable("source_listings");

        builder.HasKey(listing => listing.Id);
        builder.Property(listing => listing.Id).HasColumnType("bigint").ValueGeneratedNever();

        builder.Property(listing => listing.Url).HasMaxLength(2_048).IsRequired();
        builder.Property(listing => listing.Domain).HasMaxLength(255).IsRequired();
        builder.Property(listing => listing.SourceId).HasMaxLength(255);
        builder.Property(listing => listing.Title).HasMaxLength(512).IsRequired();
        builder.Property(listing => listing.ImageUrls).HasColumnType("text[]").IsRequired();
        builder.Property(listing => listing.Rights).HasMaxLength(255).IsRequired();
        builder.Property(listing => listing.StagedRecordId).HasColumnType("bigint").IsRequired();
        builder.Property(listing => listing.CollectedAt).IsRequired();

        builder.Ignore(listing => listing.DomainEvents);

        builder.HasIndex(listing => listing.Url)
            .IsUnique()
            .HasDatabaseName("ux_source_listings_url");

        // Filtered, because a listing without the shop's own id has nothing to be unique on and
        // NULLs would otherwise collide or not depending on the server's mood.
        builder.HasIndex(listing => new { listing.Domain, listing.SourceId })
            .IsUnique()
            .HasFilter("source_id IS NOT NULL")
            .HasDatabaseName("ux_source_listings_domain_source");

        builder.OwnsMany(listing => listing.Observations, observation =>
        {
            observation.ToTable("price_observations", table => table.HasCheckConstraint(
                "ck_price_observations_amount", "amount_minor > 0"));

            observation.WithOwner().HasForeignKey("source_listing_id");
            observation.HasKey(value => value.Id);
            observation.Property(value => value.Id).HasColumnType("bigint").ValueGeneratedNever();

            observation.Property(value => value.VariantKey).HasMaxLength(128).IsRequired();
            observation.Property(value => value.AmountMinor).HasColumnType("bigint").IsRequired();
            observation.Property(value => value.Currency).HasColumnType("char(3)").IsRequired();
            observation.Property(value => value.CollectedAt).IsRequired();

            // One observation per variant per instant: a redelivered step must not make a flat
            // price look like a flurry of changes.
            observation.HasIndex(
                    "source_listing_id",
                    nameof(PriceObservation.VariantKey),
                    nameof(PriceObservation.CollectedAt))
                .IsUnique()
                .HasDatabaseName("ux_price_observations_grain");
        });
    }
}
