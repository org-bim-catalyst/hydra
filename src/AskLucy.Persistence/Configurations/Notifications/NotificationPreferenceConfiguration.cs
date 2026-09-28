using AskLucy.Domain.Notifications;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Notifications;

/// <summary>EF Core mapping for <see cref="NotificationPreference"/>: sparse per-user overrides of the catalogue defaults (FR-031–FR-034).</summary>
public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.UserId).HasMaxLength(450).IsRequired();
        builder.Property(p => p.Category).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Frequency).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        // Also serves the UserId foreign key.
        builder.HasIndex(p => new { p.UserId, p.Category, p.Channel })
            .IsUnique()
            .HasDatabaseName("UX_NotificationPreferences_User_Category_Channel");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
