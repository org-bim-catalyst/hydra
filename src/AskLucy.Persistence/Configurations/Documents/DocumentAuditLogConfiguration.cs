using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

/// <summary>Append-only, deliberately distinct from <see cref="DocumentProcessingLogConfiguration"/> (FR-051) — no FK constraint on <c>DocumentId</c> (nullable; an audit entry for a document that was never created, e.g. a rejected upload, or one later purged, is retained).</summary>
public sealed class DocumentAuditLogConfiguration : IEntityTypeConfiguration<DocumentAuditLog>
{
    public void Configure(EntityTypeBuilder<DocumentAuditLog> builder)
    {
        builder.ToTable("DocumentAuditLogs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.ActorUserId).IsRequired();
        builder.Property(a => a.EventType).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Detail).HasMaxLength(2000);
        builder.Property(a => a.OccurredAtUtc).IsRequired();

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => new { a.DocumentId, a.OccurredAtUtc });
    }
}
