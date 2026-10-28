using AuthorizationService.Application.Entities;
using AuthorizationService.Implementation.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorizationService.Implementation.Persistence.Configurations;

internal sealed class PermissionRuleConfiguration : IEntityTypeConfiguration<PermissionRule>
{
    public void Configure(EntityTypeBuilder<PermissionRule> builder)
    {
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

        // One rule per permission, naming the lowest role that satisfies it, and no scope rows:
        // backoffice permissions are global. A rule with scopes would require the assignment to
        // match one of them, which is the opposite of what these codes mean.
        builder.HasData(AdminPermissionMatrix.Rows.Select(row => new
        {
            Id = row.RuleId,
            PermissionId = row.PermissionId,
            RoleId = (Guid?)row.RoleId,
        }));
    }
}
