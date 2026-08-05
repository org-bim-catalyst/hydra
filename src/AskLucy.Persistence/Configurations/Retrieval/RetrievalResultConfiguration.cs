using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Retrieval;

/// <summary>Append-only (data-model.md).</summary>
public sealed class RetrievalResultConfiguration : IEntityTypeConfiguration<RetrievalResult>
{
    public void Configure(EntityTypeBuilder<RetrievalResult> builder)
    {
        builder.ToTable("RetrievalResults");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Rank).IsRequired();
        builder.Property(r => r.RelevanceScore).IsRequired().HasPrecision(9, 6);
        builder.Property(r => r.SemanticScore).HasPrecision(9, 6);
        builder.Property(r => r.KeywordScore).HasPrecision(9, 6);

        builder.Property(r => r.CreatedBy).IsRequired();
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasIndex(r => r.RetrievalHistoryId);
        builder.HasIndex(r => r.DocumentChunkId);

        builder.HasOne<RetrievalHistory>()
            .WithMany()
            .HasForeignKey(r => r.RetrievalHistoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<DocumentChunk>()
            .WithMany()
            .HasForeignKey(r => r.DocumentChunkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
