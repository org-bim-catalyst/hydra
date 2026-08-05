using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentProcessingJobConfiguration : IEntityTypeConfiguration<DocumentProcessingJob>
{
    public void Configure(EntityTypeBuilder<DocumentProcessingJob> builder)
    {
        builder.ToTable("DocumentProcessingJobs");

        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();

        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(j => j.HangfireJobId).HasMaxLength(50);
        builder.Property(j => j.FailureReason).HasMaxLength(2000);
        builder.Property(j => j.RetryCount).IsRequired().HasDefaultValue(0);

        builder.Property(j => j.CreatedBy).IsRequired();
        builder.Property(j => j.RowVersion).IsRowVersion();

        builder.HasIndex(j => j.DocumentId);
        builder.HasIndex(j => j.DocumentVersionId);
        builder.HasIndex(j => j.Status);

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(j => j.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<DocumentVersion>()
            .WithMany()
            .HasForeignKey(j => j.DocumentVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
