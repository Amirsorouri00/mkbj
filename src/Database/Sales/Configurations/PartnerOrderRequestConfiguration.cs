using Mkbj.Modules.Sales.Application.Domain.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mkbj.Database.Sales.Configurations;

internal sealed class PartnerOrderRequestConfiguration : IEntityTypeConfiguration<PartnerOrderRequest>
{
    public void Configure(EntityTypeBuilder<PartnerOrderRequest> builder)
    {
        builder.ToTable("partner_order_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Path).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RequestBody).HasColumnType("text").IsRequired();
        builder.Property(x => x.ResponseBody).HasColumnType("text");
        builder.Property(x => x.ExternalOrderIds).HasColumnType("text[]");
        builder.HasIndex(x => new { x.PartnerId, x.Id });
        builder.HasIndex(x => x.CorrelationId);
    }
}
