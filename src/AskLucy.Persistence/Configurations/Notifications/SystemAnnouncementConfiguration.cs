using AskLucy.Domain.Notifications;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Notifications;

/// <summary>EF Core mapping for <see cref="SystemAnnouncement"/> (FR-004a). Published announcements are immutable.</summary>
public sealed class SystemAnnouncementConfiguration : IEntityTypeConfiguration<SystemAnnouncement>
{
    public void Configure(EntityTypeBuilder<SystemAnnouncement> builder)
    {
        builder.ToTable("SystemAnnouncements");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.Title).HasMaxLength(SystemAnnouncement.TitleMaxLength).IsRequired();
        builder.Property(a => a.Message).HasMaxLength(SystemAnnouncement.MessageMaxLength).IsRequired();
        builder.Property(a => a.Audience).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.PublishedByUserId).HasMaxLength(450).IsRequired();

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => a.PublishedAtUtc)
            .IsDescending()
            .HasDatabaseName("IX_SystemAnnouncements_PublishedAt");

        // Restrict: an announcement is a record of what was sent, so it outlives nothing silently.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.PublishedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.PublishedByUserId);
    }
}
