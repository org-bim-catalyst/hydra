using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Notifications;

/// <summary>EF Core mapping for <see cref="NotificationTemplate"/>: one row per (type, channel, language) (FR-038).</summary>
public sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("NotificationTemplates");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Type).HasMaxLength(Notification.TypeMaxLength).IsRequired();
        builder.Property(t => t.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Language).HasMaxLength(NotificationTemplate.LanguageMaxLength).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(NotificationTemplate.NameMaxLength).IsRequired();
        builder.Property(t => t.Category).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(t => t.CreatedBy).IsRequired();
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasIndex(t => new { t.Type, t.Channel, t.Language })
            .IsUnique()
            .HasDatabaseName("UX_NotificationTemplates_Type_Channel_Language");

        builder.HasMany(t => t.Versions)
            .WithOne()
            .HasForeignKey(v => v.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Nullable, and no cascade, to break the template <-> version cycle; set on publish.
        builder.HasOne<NotificationTemplateVersion>()
            .WithMany()
            .HasForeignKey(t => t.PublishedVersionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(t => t.PublishedVersionId);
    }
}
