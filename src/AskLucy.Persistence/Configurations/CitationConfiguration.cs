using AskLucy.Domain.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Citation"/> — a child of <see cref="Message"/>'s aggregate, no top-level `DbSet` (constitution &#167;5; see MessageConfiguration for the owning navigation).</summary>
public sealed class CitationConfiguration : IEntityTypeConfiguration<Citation>
{
    public void Configure(EntityTypeBuilder<Citation> builder)
    {
        builder.ToTable("Citations");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.SourceLabel).IsRequired().HasMaxLength(500);
        builder.Property(c => c.SourceReference).HasMaxLength(2048);

        // RAG-specific reference fields (spec.md FR-030, research.md Decision 9) — null for any
        // non-RAG citation source; a soft-reference, so a later-deleted chunk never breaks this row.
        builder.Property(c => c.Section).HasMaxLength(500);

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasQueryFilter(c => c.DeletedAtUtc == null);

        builder.HasIndex(c => c.MessageId);
        builder.HasIndex(c => c.DocumentChunkId);
    }
}
