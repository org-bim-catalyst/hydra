using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Prompts;

/// <summary>Has its own top-level `DbSet` (unlike PromptVariable) because `ListTagsQuery` needs a cross-prompt distinct query, not just per-aggregate navigation access — mirrors `KnowledgeBaseTagConfiguration`.</summary>
public sealed class PromptTagConfiguration : IEntityTypeConfiguration<PromptTag>
{
    public void Configure(EntityTypeBuilder<PromptTag> builder)
    {
        builder.ToTable("PromptTags");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.OwnerId).IsRequired();
        builder.Property(t => t.Value).IsRequired().HasMaxLength(50);

        builder.Property(t => t.CreatedBy).IsRequired();
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasQueryFilter(t => t.DeletedAtUtc == null);

        builder.HasIndex(t => new { t.OwnerId, t.Value });
        builder.HasIndex(t => t.PromptId);
    }
}
