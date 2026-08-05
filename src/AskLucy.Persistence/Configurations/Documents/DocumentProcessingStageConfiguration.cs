using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentProcessingStageConfiguration : IEntityTypeConfiguration<DocumentProcessingStage>
{
    public void Configure(EntityTypeBuilder<DocumentProcessingStage> builder)
    {
        builder.ToTable("DocumentProcessingStages");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.StageType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.FailureReason).HasMaxLength(2000);

        builder.Property(s => s.CreatedBy).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => s.DocumentProcessingJobId);
        builder.HasIndex(s => new { s.DocumentProcessingJobId, s.StageType }).IsUnique();

        builder.HasOne<DocumentProcessingJob>()
            .WithMany()
            .HasForeignKey(s => s.DocumentProcessingJobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
