using AuthorizationService.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorizationService.Implementation.Persistence.Configurations;

internal sealed class PermissionRuleConfiguration : IEntityTypeConfiguration<PermissionRule>
{
    public void Configure(EntityTypeBuilder<PermissionRule> builder)
    {
        builder.ToTable("PermissionRules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.HasOne(r => r.Role)
            .WithMany()
            .HasForeignKey(r => r.RoleId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasMany(r => r.Scopes)
            .WithOne(rs => rs.PermissionRule)
            .HasForeignKey(rs => rs.PermissionRuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
