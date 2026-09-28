using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Notifications;

/// <summary>
/// EF Core mapping for <see cref="NotificationOutboxEvent"/> (research R2). No soft-delete filter:
/// retention hard-deletes completed rows.
/// </summary>
public sealed class NotificationOutboxEventConfiguration : IEntityTypeConfiguration<NotificationOutboxEvent>
{
    public void Configure(EntityTypeBuilder<NotificationOutboxEvent> builder)
    {
        builder.ToTable("NotificationOutboxEvents");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Type).HasMaxLength(NotificationOutboxEvent.TypeMaxLength).IsRequired();
        builder.Property(e => e.EventKey).HasMaxLength(NotificationOutboxEvent.EventKeyMaxLength);
        builder.Property(e => e.RecipientJson).IsRequired();
        builder.Property(e => e.VariablesJson).IsRequired();
        builder.Property(e => e.RelatedItemType).HasMaxLength(NotificationOutboxEvent.RelatedItemTypeMaxLength);
        builder.Property(e => e.RelatedItemId).HasMaxLength(NotificationOutboxEvent.RelatedItemIdMaxLength);
        builder.Property(e => e.RelatedItemParentId).HasMaxLength(NotificationOutboxEvent.RelatedItemIdMaxLength);
        builder.Property(e => e.ExplicitLanguage).HasMaxLength(NotificationOutboxEvent.LanguageMaxLength);
        builder.Property(e => e.CorrelationId).HasMaxLength(NotificationOutboxEvent.CorrelationIdMaxLength).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.LeaseOwner).HasMaxLength(NotificationOutboxEvent.LeaseOwnerMaxLength);
        builder.Property(e => e.FanOutCursor).HasMaxLength(NotificationOutboxEvent.FanOutCursorMaxLength);
        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.LastError).HasMaxLength(NotificationOutboxEvent.LastErrorMaxLength);

        builder.Property(e => e.CreatedBy).IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();

        // The dispatcher's claim scan (R4): due pending rows, and processing rows whose lease expired.
        builder.HasIndex(e => new { e.Status, e.NextAttemptAtUtc, e.OccurredAtUtc })
            .IncludeProperties(e => e.LeaseExpiresAtUtc)
            .HasFilter("[Status] IN (N'Pending', N'Processing')")
            .HasDatabaseName("IX_NotificationOutboxEvents_Due");

        builder.HasIndex(e => e.ProcessedAtUtc)
            .HasFilter("[Status] = N'Completed'")
            .HasDatabaseName("IX_NotificationOutboxEvents_Processed");

        // Deliberately not unique: de-duplication happens at materialization, and a repeated key
        // must never roll back the emitter's own transaction (data-model.md).
        builder.HasIndex(e => e.EventKey)
            .HasFilter("[EventKey] IS NOT NULL")
            .HasDatabaseName("IX_NotificationOutboxEvents_EventKey");
    }
}
