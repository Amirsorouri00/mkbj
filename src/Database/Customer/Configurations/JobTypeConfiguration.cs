using EwaysVNext.Modules.Customer.Application.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EwaysVNext.Database.Customer.Configurations;

internal sealed class JobTypeConfiguration : IEntityTypeConfiguration<JobType>
{
    public void Configure(EntityTypeBuilder<JobType> builder)
    {
        builder.ToTable("JobType");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id)
            .UseIdentityColumn(1, 1)
            .IsRequired();

        builder.Property(j => j.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasData(
            JobType.Create(1, "دفتر پیشخوان"),
            JobType.Create(2, "فروشگاه موبایل و تعمیرات"),
            JobType.Create(3, "دفتر فنی"),
            JobType.Create(4, "کافی نت"),
            JobType.Create(5, "لوازم التحریر"),
            JobType.Create(6, "خدمات کامپیوتری"),
            JobType.Create(7, "فروشگاه کارت شارژ"),
            JobType.Create(8, "نمایندگی اپراتورها"),
            JobType.Create(9, "سایر"),
            JobType.Create(10, "روستایی ICT"),
            JobType.Create(11, "DRP"),
            JobType.Create(12, "انجمن دفاتر پیشخوان استان تهران"),
            JobType.Create(13, "B2C"));
    }
}
