using AskLucy.Domain.KnowledgeBases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>Has its own top-level `DbSet` (unlike Attachment/Citation) because <c>ListTagsQuery</c> needs a cross-knowledge-base distinct query, not just per-aggregate navigation access — mirrors why <c>Message</c> (not Attachment/Citation) has its own `DbSet` too.</summary>
public sealed class KnowledgeBaseTagConfiguration : IEntityTypeConfiguration<KnowledgeBaseTag>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseTag> builder)
    {
        builder.ToTable("KnowledgeBaseTags");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.OwnerId).IsRequired();
        builder.Property(t => t.Value).IsRequired().HasMaxLength(50);

        builder.Property(t => t.CreatedBy).IsRequired();
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasQueryFilter(t => t.DeletedAtUtc == null);

        builder.HasIndex(t => new { t.OwnerId, t.Value });
        builder.HasIndex(t => t.KnowledgeBaseId);
    }
}
