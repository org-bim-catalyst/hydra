using AskLucy.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

public sealed class RoleAuditLogConfiguration : IEntityTypeConfiguration<RoleAuditLog>
{
    public void Configure(EntityTypeBuilder<RoleAuditLog> builder)
    {
        builder.Property(a => a.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(a => a.TargetRoleId).HasMaxLength(450);
        builder.Property(a => a.TargetRoleName).HasMaxLength(50);
        builder.Property(a => a.TargetUserId).HasMaxLength(450);
        builder.Property(a => a.DetailsJson).IsRequired();
        builder.Property(a => a.CorrelationId).HasMaxLength(64);

        builder.HasIndex(a => a.OccurredAtUtc);
        builder.HasIndex(a => a.TargetRoleId);
        builder.HasIndex(a => a.TargetUserId);
        builder.HasIndex(a => a.ActorUserId);
        builder.HasIndex(a => a.CorrelationId);
    }
}
