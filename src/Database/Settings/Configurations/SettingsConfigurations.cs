using EwaysVNext.Modules.Settings.Application.Settings.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Settings.Configurations;

/// <summary>
/// Every settings table is a singleton, enforced by <c>CHECK (id = 1)</c> rather than by
/// convention. A second row would not fail anywhere obvious — the repository fetches by id, so a
/// stray row would simply be ignored, and an admin would edit a record nothing ever reads.
/// </summary>
internal static class SingletonTable
{
    public static void Constrain(TableBuilder table, string name) =>
        table.HasCheckConstraint($"ck_{name}_singleton", "id = 1");
}

internal sealed class SellerProfileConfiguration : IEntityTypeConfiguration<SellerProfile>
{
    public void Configure(EntityTypeBuilder<SellerProfile> builder)
    {
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).ValueGeneratedNever();

        builder.Property(profile => profile.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.Uen).HasMaxLength(32).IsRequired();
        builder.Property(profile => profile.Address).HasMaxLength(500).IsRequired();
        builder.Property(profile => profile.Email).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.GstRegistered).IsRequired();
        builder.Property(profile => profile.InvoiceFooter).HasMaxLength(1000).IsRequired();

        builder.PrimitiveCollection<IReadOnlyList<string>>("RequiredBuyerFields")
            .HasField("_requiredBuyerFields")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("required_buyer_fields")
            .IsRequired();

        builder.ToTable("seller_profile", table => SingletonTable.Constrain(table, "seller_profile"));

        builder.HasData(new
        {
            Id = SettingsDefaults.SingletonId,
            LegalName = SettingsDefaults.LegalName,
            Uen = SettingsDefaults.Uen,
            Address = SettingsDefaults.Address,
            Email = SettingsDefaults.Email,
            GstRegistered = false,
            InvoiceFooter = SettingsDefaults.InvoiceFooter,
            // A List, not an array: the backing field is List<string>, and EF materialises seed
            // data by assigning straight into it. An array here throws InvalidCastException for
            // every InMemory test in the solution, not just the ones touching settings.
            RequiredBuyerFields = new List<string> { SettingsDefaults.RequiredBuyerFieldsEntry },
        });
    }
}

internal sealed class PaymentInstructionConfiguration : IEntityTypeConfiguration<PaymentInstruction>
{
    public void Configure(EntityTypeBuilder<PaymentInstruction> builder)
    {
        builder.HasKey(instruction => instruction.Id);
        builder.Property(instruction => instruction.Id).ValueGeneratedNever();
        builder.Property(instruction => instruction.ActiveVersion).IsRequired();

        builder.HasMany(instruction => instruction.Versions)
            .WithOne()
            .HasForeignKey(version => version.PaymentInstructionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(instruction => instruction.Versions)
            .HasField("_versions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.ToTable("payment_instruction", table => SingletonTable.Constrain(table, "payment_instruction"));

        builder.HasData(new { Id = SettingsDefaults.SingletonId, ActiveVersion = 1 });
    }
}

internal sealed class PaymentInstructionVersionConfiguration : IEntityTypeConfiguration<PaymentInstructionVersion>
{
    private static readonly DateTimeOffset SeedTimestamp =
        new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<PaymentInstructionVersion> builder)
    {
        builder.HasKey(version => version.Id);
        builder.Property(version => version.Id).ValueGeneratedNever();

        builder.Property(version => version.PaymentInstructionId).IsRequired();
        builder.Property(version => version.Version).IsRequired();
        builder.Property(version => version.Text).HasMaxLength(4000).IsRequired();
        builder.Property(version => version.CreatedBy);
        builder.Property(version => version.CreatedAt).IsRequired();

        // Two rows claiming the same version number would make "which wording was invoice 42
        // issued under" unanswerable.
        builder.HasIndex(version => new { version.PaymentInstructionId, version.Version }).IsUnique();

        builder.ToTable("payment_instruction_versions");

        builder.HasData(new
        {
            Id = SettingsDefaults.SingletonId,
            PaymentInstructionId = SettingsDefaults.SingletonId,
            Version = 1,
            Text = SettingsDefaults.InitialPaymentInstructionText,
            CreatedBy = (Guid?)null,
            CreatedAt = SeedTimestamp,
        });
    }
}

internal sealed class CommerceLimitsConfiguration : IEntityTypeConfiguration<CommerceLimits>
{
    public void Configure(EntityTypeBuilder<CommerceLimits> builder)
    {
        builder.HasKey(limits => limits.Id);
        builder.Property(limits => limits.Id).ValueGeneratedNever();

        builder.Property(limits => limits.MinUsdMinor).IsRequired();
        builder.Property(limits => limits.MaxUsdMinor).IsRequired();
        builder.Property(limits => limits.BulkDefaultMax).IsRequired();
        builder.Property(limits => limits.CompositionDeadlineMs).IsRequired();
        builder.Property(limits => limits.CompositionMaxLines).IsRequired();

        builder.ToTable("commerce_limits", table =>
        {
            SingletonTable.Constrain(table, "commerce_limits");

            // Repeated from the domain because the domain only guards the application's path: a
            // fix-up script writing the row directly would not pass through it.
            table.HasCheckConstraint("ck_commerce_limits_range", "min_usd_minor > 0 AND max_usd_minor > min_usd_minor");
            table.HasCheckConstraint("ck_commerce_limits_deadline", "composition_deadline_ms BETWEEN 1000 AND 9500");
        });

        builder.HasData(new
        {
            Id = SettingsDefaults.SingletonId,
            MinUsdMinor = SettingsDefaults.MinUsdMinor,
            MaxUsdMinor = SettingsDefaults.MaxUsdMinor,
            BulkDefaultMax = SettingsDefaults.BulkDefaultMax,
            CompositionDeadlineMs = SettingsDefaults.CompositionDeadlineMs,
            CompositionMaxLines = SettingsDefaults.CompositionMaxLines,
        });
    }
}
