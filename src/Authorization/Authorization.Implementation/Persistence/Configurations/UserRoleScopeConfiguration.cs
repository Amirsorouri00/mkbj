using AuthorizationService.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorizationService.Implementation.Persistence.Configurations;

internal sealed class UserRoleScopeConfiguration : IEntityTypeConfiguration<UserRoleScope>
{
    public void Configure(EntityTypeBuilder<UserRoleScope> builder)
    {
        builder.ToTable("UserRoleScopes");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.CreatedAtUtc).IsRequired();

        builder.HasOne(a => a.Role)
            .WithMany()
            .HasForeignKey(a => a.RoleId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(a => a.Scope)
            .WithMany()
            .HasForeignKey(a => a.ScopeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(a => new { a.UserId, a.RoleId, a.ScopeId }).IsUnique();
    }
}
