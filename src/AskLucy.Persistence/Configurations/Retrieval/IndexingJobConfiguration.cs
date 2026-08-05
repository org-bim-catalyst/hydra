using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Retrieval;

public sealed class IndexingJobConfiguration : IEntityTypeConfiguration<IndexingJob>
{
    public void Configure(EntityTypeBuilder<IndexingJob> builder)
    {
        builder.ToTable("IndexingJobs");

        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();

        builder.Property(j => j.JobType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(j => j.RetryCount).IsRequired().HasDefaultValue(0);
        builder.Property(j => j.MaxRetries).IsRequired();
        builder.Property(j => j.HangfireJobId).HasMaxLength(100);
        builder.Property(j => j.FailureReason).HasMaxLength(2000);

        builder.Property(j => j.CreatedBy).IsRequired();
        builder.Property(j => j.RowVersion).IsRowVersion();

        builder.HasQueryFilter(j => j.DeletedAtUtc == null);

        builder.HasIndex(j => j.KnowledgeBaseId);
        builder.HasIndex(j => new { j.KnowledgeBaseId, j.Status });
        builder.HasIndex(j => j.KnowledgeBaseDocumentId);

        builder.HasOne<KnowledgeBase>()
            .WithMany()
            .HasForeignKey(j => j.KnowledgeBaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<KnowledgeBaseDocument>()
            .WithMany()
            .HasForeignKey(j => j.KnowledgeBaseDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
