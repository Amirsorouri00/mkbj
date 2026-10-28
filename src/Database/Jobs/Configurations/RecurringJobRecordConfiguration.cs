using EwaysVNext.Jobs.Implementation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Jobs.Configurations;

internal sealed class RecurringJobRecordConfiguration : IEntityTypeConfiguration<RecurringJobRecord>
{
    public void Configure(EntityTypeBuilder<RecurringJobRecord> builder)
    {
        // See JobRecordConfiguration: the recurring tick is raw SQL too.
        builder.ToTable("recurring");

        builder.HasKey(recurring => recurring.Type);
        builder.Property(recurring => recurring.Type).HasMaxLength(100).ValueGeneratedNever();
        builder.Property(recurring => recurring.Interval).HasColumnType("interval").IsRequired();
        builder.Property(recurring => recurring.NextRunAt).IsRequired();
    }
}
