using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Customer.Configurations;

internal sealed class LegalInformationDocumentConfiguration : IEntityTypeConfiguration<LegalInformationDocument>
{
    public void Configure(EntityTypeBuilder<LegalInformationDocument> builder)
    {
        builder.ToTable("LegalInformationDocument");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.LegalInformationId).IsRequired();
        builder.Property(d => d.Type).HasColumnType("int").IsRequired();
        builder.Property(d => d.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(d => d.FileName).HasMaxLength(260).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(d => d.SizeBytes).IsRequired();
        builder.Property(d => d.CreatedAt).HasColumnType("datetime2(3)").IsRequired();

        builder.HasOne<LegalInformation>()
            .WithMany(l => l.Documents)
            .HasForeignKey(d => d.LegalInformationId)
            .OnDelete(DeleteBehavior.Cascade);

        // One document per type (introduction letter / official gazette) per legal-information
        // record - re-uploading replaces the previous one instead of accumulating rows.
        builder.HasIndex(d => new { d.LegalInformationId, d.Type }).IsUnique();
    }
}
