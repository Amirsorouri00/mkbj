using AuthorizationService.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorizationService.Implementation.Persistence.Configurations;

internal sealed class PermissionRuleScopeConfiguration : IEntityTypeConfiguration<PermissionRuleScope>
{
    public void Configure(EntityTypeBuilder<PermissionRuleScope> builder)
    {
        builder.ToTable("PermissionRuleScopes");
        builder.HasKey(rs => new { rs.PermissionRuleId, rs.ScopeId });

        builder.HasOne(rs => rs.Scope)
            .WithMany()
            .HasForeignKey(rs => rs.ScopeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
