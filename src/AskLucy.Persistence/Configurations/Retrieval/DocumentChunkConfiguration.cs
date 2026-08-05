using AskLucy.Domain.Documents;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Retrieval;

/// <summary>EF Core mapping for <see cref="DocumentChunk"/> — a full-text index on <see cref="DocumentChunk.Content"/> backs keyword/hybrid search (research.md Decision 6); created via raw SQL in the migration (EF's Fluent API has no first-class full-text-index builder).</summary>
public sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.ToTable("DocumentChunks");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.ChunkingStrategy).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Content).IsRequired();
        builder.Property(c => c.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(c => c.TokenCount).IsRequired();
        builder.Property(c => c.CharacterCount).IsRequired();
        builder.Property(c => c.Language).HasMaxLength(35);
        builder.Property(c => c.Section).HasMaxLength(500);
        builder.Property(c => c.Heading).HasMaxLength(500);
        builder.Property(c => c.Position).IsRequired();

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasQueryFilter(c => c.DeletedAtUtc == null);

        builder.HasIndex(c => c.KnowledgeBaseId);
        builder.HasIndex(c => c.KnowledgeBaseDocumentId);
        builder.HasIndex(c => c.DocumentVersionId);
        builder.HasIndex(c => c.ContentHash);
        builder.HasIndex(c => new { c.DocumentVersionId, c.Position });

        builder.HasOne<KnowledgeBase>()
            .WithMany()
            .HasForeignKey(c => c.KnowledgeBaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<KnowledgeBaseDocument>()
            .WithMany()
            .HasForeignKey(c => c.KnowledgeBaseDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DocumentVersion>()
            .WithMany()
            .HasForeignKey(c => c.DocumentVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
