using AskLucy.Domain.Agents;
using AskLucy.Domain.KnowledgeBases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

public sealed class AgentKnowledgeBaseConfiguration : IEntityTypeConfiguration<AgentKnowledgeBase>
{
    public void Configure(EntityTypeBuilder<AgentKnowledgeBase> builder)
    {
        builder.ToTable("AgentKnowledgeBases");

        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();

        builder.Property(k => k.CreatedBy).IsRequired();
        builder.Property(k => k.RowVersion).IsRowVersion();

        builder.HasIndex(k => new { k.AgentId, k.KnowledgeBaseId }).IsUnique();

        // Real FK for referential integrity; a normal (soft) KnowledgeBase delete leaves this
        // row untouched (the KB row itself still exists) — FR-049 re-validates access
        // per-execution regardless, so a soft-deleted-but-still-configured KB simply drops out
        // of the caller's authorized set at execution time. Cascade only fires on a genuine
        // hard/purge delete of the KnowledgeBase.
        builder.HasOne<KnowledgeBase>()
            .WithMany()
            .HasForeignKey(k => k.KnowledgeBaseId)
            .OnDelete(DeleteBehavior.Cascade);

        // The Agent <-> AgentKnowledgeBase relationship is configured from AgentConfiguration.
    }
}
