using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Notifications;

/// <summary>
/// EF Core mapping for <see cref="NotificationAuditLog"/>, append-only (FR-037, FR-054). The actor
/// is a plain string, not a foreign key, so the audit trail survives account erasure.
/// </summary>
public sealed class NotificationAuditLogConfiguration : IEntityTypeConfiguration<NotificationAuditLog>
{
    public void Configure(EntityTypeBuilder<NotificationAuditLog> builder)
    {
        builder.ToTable("NotificationAuditLogs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.ActorUserId).HasMaxLength(450);
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(60).IsRequired();
        builder.Property(a => a.TargetType).HasMaxLength(NotificationAuditLog.TargetTypeMaxLength).IsRequired();
        builder.Property(a => a.TargetId).HasMaxLength(NotificationAuditLog.TargetIdMaxLength).IsRequired();
        builder.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.CorrelationId).HasMaxLength(Notification.CorrelationIdMaxLength);

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => a.OccurredAtUtc)
            .IsDescending()
            .HasDatabaseName("IX_NotificationAuditLogs_OccurredAt");

        builder.HasIndex(a => new { a.TargetType, a.TargetId })
            .HasDatabaseName("IX_NotificationAuditLogs_Target");
    }
}
