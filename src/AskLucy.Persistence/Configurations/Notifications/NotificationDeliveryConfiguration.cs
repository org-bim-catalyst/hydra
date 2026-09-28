using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Notifications;

/// <summary>
/// EF Core mapping for <see cref="NotificationDelivery"/>, a child of <see cref="Notification"/>
/// with no <c>DbSet</c> (constitution §5). Its <c>Id</c> is the delivery identity (FR-028).
/// </summary>
public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("NotificationDeliveries");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.RecipientKind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.RecipientAddress).HasMaxLength(NotificationDelivery.RecipientAddressMaxLength);
        builder.Property(d => d.Language).HasMaxLength(Notification.LanguageMaxLength);
        builder.Property(d => d.LeaseOwner).HasMaxLength(200);
        builder.Property(d => d.SkipReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.FailureKind).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.FailureReason).HasMaxLength(NotificationDelivery.FailureReasonMaxLength);
        builder.Property(d => d.ProviderResponse).HasMaxLength(NotificationDelivery.ProviderResponseMaxLength);
        builder.Property(d => d.CorrelationId).HasMaxLength(Notification.CorrelationIdMaxLength).IsRequired();

        builder.Property(d => d.CreatedBy).IsRequired();
        builder.Property(d => d.RowVersion).IsRowVersion();

        // The worker's claim scan (R4); the sweeper reads the Sending rows through it too. Priority is
        // stored as a string, so its alphabetical order is meaningless: it is included rather than keyed,
        // and the worker ranks the due batch by priority itself.
        builder.HasIndex(d => new { d.Status, d.NextAttemptAtUtc })
            .IncludeProperties(d => new { d.Priority, d.Channel, d.LeaseExpiresAtUtc })
            .HasFilter("[Status] IN (N'Pending', N'Retrying', N'Sending')")
            .HasDatabaseName("IX_NotificationDeliveries_Queue");

        builder.HasIndex(d => new { d.Status, d.LastAttemptAtUtc })
            .IsDescending(false, true)
            .HasFilter("[Status] IN (N'Failed', N'DeadLettered')")
            .HasDatabaseName("IX_NotificationDeliveries_Failed");

        // One delivery per channel per notification; also serves the NotificationId foreign key.
        builder.HasIndex(d => new { d.NotificationId, d.Channel })
            .IsUnique()
            .HasDatabaseName("UX_NotificationDeliveries_Notification_Channel");

        builder.HasOne<NotificationTemplateVersion>()
            .WithMany()
            .HasForeignKey(d => d.TemplateVersionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(d => d.TemplateVersionId);
    }
}
