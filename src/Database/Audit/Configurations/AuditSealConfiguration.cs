using EwaysVNext.Audit.Implementation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Audit.Configurations;

internal sealed class AuditSealConfiguration : IEntityTypeConfiguration<AuditSeal>
{
    public void Configure(EntityTypeBuilder<AuditSeal> builder)
    {
        builder.HasKey(seal => seal.EventId);
        builder.Property(seal => seal.EventId).ValueGeneratedNever();
        builder.Property(seal => seal.Hash).IsRequired();
        builder.Property(seal => seal.SealedAt).IsRequired();
    }
}
