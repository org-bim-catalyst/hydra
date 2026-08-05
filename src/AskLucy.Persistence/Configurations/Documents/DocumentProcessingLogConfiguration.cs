using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

/// <summary>Append-only — no soft delete, no FK cascade behavior configured beyond an index (data-model.md: forms the visible processing history, never updated/deleted).</summary>
public sealed class DocumentProcessingLogConfiguration : IEntityTypeConfiguration<DocumentProcessingLog>
{
    public void Configure(EntityTypeBuilder<DocumentProcessingLog> builder)
    {
        builder.ToTable("DocumentProcessingLogs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.EventType).IsRequired().HasMaxLength(50);
        builder.Property(l => l.Detail).HasMaxLength(2000);
        builder.Property(l => l.OccurredAtUtc).IsRequired();

        builder.Property(l => l.CreatedBy).IsRequired();
        builder.Property(l => l.RowVersion).IsRowVersion();

        builder.HasIndex(l => new { l.DocumentId, l.OccurredAtUtc });
        builder.HasIndex(l => l.DocumentProcessingJobId);
    }
}
