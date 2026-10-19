using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>specs/082 — EF Core mapping for the append-only provider health-check retention audit trail.</summary>
public sealed class ProviderHealthRetentionAuditLogConfiguration : IEntityTypeConfiguration<ProviderHealthRetentionAuditLog>
{
    public void Configure(EntityTypeBuilder<ProviderHealthRetentionAuditLog> builder)
    {
        builder.ToTable("ProviderHealthRetentionAuditLogs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.ActorUserId).HasMaxLength(ProviderHealthRetentionAuditLog.ActorUserIdMaxLength).IsRequired();
        builder.Property(a => a.PreviousRetentionDays).IsRequired();
        builder.Property(a => a.NewRetentionDays).IsRequired();
        builder.Property(a => a.OccurredAtUtc).IsRequired();

        builder.Property(a => a.CorrelationId).HasMaxLength(64);

        builder.Property(a => a.CreatedBy).IsRequired().HasMaxLength(450);
        builder.Property(a => a.ModifiedBy).HasMaxLength(450);
        builder.Property(a => a.RowVersion).IsRowVersion();
    }
}
