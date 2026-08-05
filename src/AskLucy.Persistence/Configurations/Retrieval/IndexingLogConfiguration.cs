using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Retrieval;

/// <summary>Append-only — no soft delete (data-model.md: forms the visible indexing history, never updated/deleted, mirrors <c>DocumentProcessingLog</c>).</summary>
public sealed class IndexingLogConfiguration : IEntityTypeConfiguration<IndexingLog>
{
    public void Configure(EntityTypeBuilder<IndexingLog> builder)
    {
        builder.ToTable("IndexingLogs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Stage).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.Message).HasMaxLength(2000);
        builder.Property(l => l.OccurredAtUtc).IsRequired();

        builder.Property(l => l.CreatedBy).IsRequired();
        builder.Property(l => l.RowVersion).IsRowVersion();

        builder.HasIndex(l => new { l.IndexingJobId, l.OccurredAtUtc });

        builder.HasOne<IndexingJob>()
            .WithMany()
            .HasForeignKey(l => l.IndexingJobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
