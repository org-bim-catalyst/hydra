using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentNotificationConfiguration : IEntityTypeConfiguration<DocumentNotification>
{
    public void Configure(EntityTypeBuilder<DocumentNotification> builder)
    {
        builder.ToTable("DocumentNotifications");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();

        builder.Property(n => n.UserId).IsRequired();
        builder.Property(n => n.EventType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(n => n.Message).IsRequired().HasMaxLength(500);
        builder.Property(n => n.IsRead).IsRequired().HasDefaultValue(false);

        builder.Property(n => n.CreatedBy).IsRequired();
        builder.Property(n => n.RowVersion).IsRowVersion();

        builder.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAtUtc });
        builder.HasIndex(n => n.DocumentId);
    }
}
