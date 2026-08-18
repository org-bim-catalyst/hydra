using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Memory;

/// <summary>No FK on <see cref="MemoryNotification.MemoryId"/> (nullable) — a notification must remain readable even if its memory is later deleted (research.md Decision 11).</summary>
public sealed class MemoryNotificationConfiguration : IEntityTypeConfiguration<MemoryNotification>
{
    public void Configure(EntityTypeBuilder<MemoryNotification> builder)
    {
        builder.ToTable("MemoryNotifications");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();

        builder.Property(n => n.UserId).IsRequired();
        builder.Property(n => n.EventType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(n => n.Message).IsRequired().HasMaxLength(500);

        builder.Property(n => n.CreatedBy).IsRequired();
        builder.Property(n => n.RowVersion).IsRowVersion();

        builder.HasIndex(n => new { n.UserId, n.CreatedAtUtc });
        builder.HasIndex(n => n.MemoryId);
    }
}
