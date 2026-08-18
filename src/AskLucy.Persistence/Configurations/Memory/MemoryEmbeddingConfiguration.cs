using AskLucy.Domain.Memory;
using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Persistence.Configurations.Memory;

/// <summary>
/// EF Core mapping for <see cref="MemoryEmbedding"/>. <see cref="MemoryEmbedding.Vector"/> is
/// excluded from the EF model exactly like <c>Retrieval.Embedding.Vector</c> — the actual
/// <c>vector(1536)</c> column and every read/write against it are managed via raw SQL/ADO.NET in
/// <c>SqlServerMemoryVectorStore</c> (research.md Decision 5), not through this configuration; see
/// <c>EmbeddingConfiguration</c>'s doc comment for the underlying EF Core 10.0.10 Fluent API
/// limitation this works around. No <c>CREATE VECTOR INDEX</c> either — same inherited platform
/// constraint (specs/016 research.md Decision 3).
/// </summary>
public sealed class MemoryEmbeddingConfiguration : IEntityTypeConfiguration<MemoryEmbedding>
{
    public void Configure(EntityTypeBuilder<MemoryEmbedding> builder)
    {
        builder.ToTable("MemoryEmbeddings");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Ignore(e => e.Vector);

        builder.Property(e => e.IsCurrent).IsRequired();

        builder.Property(e => e.CreatedBy).IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasQueryFilter(e => e.DeletedAtUtc == null);

        builder.HasIndex(e => e.MemoryId);
        builder.HasIndex(e => new { e.MemoryId, e.IsCurrent });
        builder.HasIndex(e => e.EmbeddingProviderId);

        builder.HasOne<MemoryEntity>()
            .WithMany()
            .HasForeignKey(e => e.MemoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Reuses the RAG feature's EmbeddingProvider catalog — Restrict, mirroring
        // EmbeddingConfiguration's identical reasoning (a deactivated provider's historical
        // embeddings must remain resolvable).
        builder.HasOne<EmbeddingProvider>()
            .WithMany()
            .HasForeignKey(e => e.EmbeddingProviderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
