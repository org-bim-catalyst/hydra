using AskLucy.Domain.Notifications;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Notifications;

/// <summary>EF Core mapping for <see cref="Notification"/> (specs/067 data-model.md). Owner deletion is soft (FR-016a).</summary>
public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();

        builder.Property(n => n.RecipientUserId).HasMaxLength(450);
        builder.Property(n => n.Category).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(n => n.Type).HasMaxLength(Notification.TypeMaxLength).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(Notification.TitleMaxLength).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(Notification.MessageMaxLength).IsRequired();
        builder.Property(n => n.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(n => n.Language).HasMaxLength(Notification.LanguageMaxLength).IsRequired();
        builder.Property(n => n.RelatedItemType).HasMaxLength(Notification.RelatedItemTypeMaxLength);
        builder.Property(n => n.RelatedItemId).HasMaxLength(Notification.RelatedItemIdMaxLength);
        builder.Property(n => n.ActionRoute).HasMaxLength(Notification.ActionRouteMaxLength);
        builder.Property(n => n.ActionLabel).HasMaxLength(Notification.ActionLabelMaxLength);
        builder.Property(n => n.CorrelationId).HasMaxLength(Notification.CorrelationIdMaxLength).IsRequired();
        builder.Property(n => n.EventKey).HasMaxLength(Notification.EventKeyMaxLength);

        builder.Property(n => n.CreatedBy).IsRequired();
        builder.Property(n => n.RowVersion).IsRowVersion();

        builder.HasQueryFilter(n => n.DeletedAtUtc == null);

        // Keyset paging for the center (SC-004), newest first.
        builder.HasIndex(n => new { n.RecipientUserId, n.CreatedAtUtc, n.Id })
            .IsDescending(false, true, true)
            .IncludeProperties(n => new { n.Category, n.ReadAtUtc, n.Priority, n.Status })
            .HasFilter("[DeletedAtUtc] IS NULL AND [ShowInCenter] = 1")
            .HasDatabaseName("IX_Notifications_Recipient_Center");

        builder.HasIndex(n => n.RecipientUserId)
            .HasFilter("[ReadAtUtc] IS NULL AND [DeletedAtUtc] IS NULL AND [ShowInCenter] = 1")
            .HasDatabaseName("IX_Notifications_Recipient_Unread");

        // De-duplication (FR-008). Deleted rows stay in both indexes, so replaying an event never
        // resurrects a notification its owner deleted.
        builder.HasIndex(n => new { n.RecipientUserId, n.EventKey })
            .IsUnique()
            .HasFilter("[EventKey] IS NOT NULL AND [RecipientUserId] IS NOT NULL")
            .HasDatabaseName("UX_Notifications_Recipient_EventKey");

        builder.HasIndex(n => n.EventKey)
            .IsUnique()
            .HasFilter("[EventKey] IS NOT NULL AND [RecipientUserId] IS NULL")
            .HasDatabaseName("UX_Notifications_Address_EventKey");

        // Retention (R20).
        builder.HasIndex(n => n.ReadAtUtc).HasFilter("[ReadAtUtc] IS NOT NULL").HasDatabaseName("IX_Notifications_Retention_Read");
        builder.HasIndex(n => n.DeletedAtUtc).HasFilter("[DeletedAtUtc] IS NOT NULL").HasDatabaseName("IX_Notifications_Retention_Deleted");

        // Hard erasure of an account takes its notifications with it; soft deletion is handled by the dispatcher (R24).
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(n => n.RecipientUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<NotificationTemplateVersion>()
            .WithMany()
            .HasForeignKey(n => n.TemplateVersionId)
            .OnDelete(DeleteBehavior.NoAction);

        // Outbox retention purges completed events; the notification keeps its content.
        builder.HasOne<NotificationOutboxEvent>()
            .WithMany()
            .HasForeignKey(n => n.SourceEventId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(n => n.TemplateVersionId);
        builder.HasIndex(n => n.SourceEventId);

        builder.HasMany(n => n.Deliveries)
            .WithOne()
            .HasForeignKey(d => d.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(n => n.Deliveries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
