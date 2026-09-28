using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Notifications;

/// <summary>
/// EF Core mapping for <see cref="NotificationTemplateVersion"/>, a child of
/// <see cref="NotificationTemplate"/> with no <c>DbSet</c> (constitution §5). Immutable once published (FR-039).
/// </summary>
public sealed class NotificationTemplateVersionConfiguration : IEntityTypeConfiguration<NotificationTemplateVersion>
{
    public void Configure(EntityTypeBuilder<NotificationTemplateVersion> builder)
    {
        builder.ToTable("NotificationTemplateVersions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(v => v.Subject).HasMaxLength(NotificationTemplateVersion.SubjectMaxLength);
        builder.Property(v => v.Preheader).HasMaxLength(NotificationTemplateVersion.PreheaderMaxLength);
        builder.Property(v => v.Greeting).HasMaxLength(NotificationTemplateVersion.GreetingMaxLength);
        builder.Property(v => v.Heading).HasMaxLength(NotificationTemplateVersion.HeadingMaxLength);
        builder.Property(v => v.SafetyNote).HasMaxLength(NotificationTemplateVersion.SafetyNoteMaxLength);
        builder.Property(v => v.FooterNote).HasMaxLength(NotificationTemplateVersion.FooterNoteMaxLength);
        builder.Property(v => v.Title).HasMaxLength(NotificationTemplateVersion.TitleMaxLength);
        builder.Property(v => v.Message).HasMaxLength(NotificationTemplateVersion.MessageMaxLength);
        builder.Property(v => v.ActionLabel).HasMaxLength(NotificationTemplateVersion.ActionLabelMaxLength);
        builder.Property(v => v.UsedVariablesJson).IsRequired();
        builder.Property(v => v.PublishedBy).HasMaxLength(450);
        builder.Property(v => v.ArchivedBy).HasMaxLength(450);

        builder.Property(v => v.CreatedBy).IsRequired();
        builder.Property(v => v.RowVersion).IsRowVersion();

        // Also serves the TemplateId foreign key.
        builder.HasIndex(v => new { v.TemplateId, v.VersionNumber })
            .IsUnique()
            .HasDatabaseName("UX_NotificationTemplateVersions_Template_Version");

        builder.HasIndex(v => v.TemplateId)
            .IsUnique()
            .HasFilter("[Status] = N'Published'")
            .HasDatabaseName("UX_NotificationTemplateVersions_OnePublished");
    }
}
