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

        // Restrict, not Cascade, on the KnowledgeBase side — SQL Server rejects two cascade paths
        // converging on the same join table ("may cause cycles or multiple cascade paths"): both
        // Agents and KnowledgeBases cascade from ApplicationUser, and both would cascade into this
        // table. Same conflict already hit and fixed the same way in
        // ConversationKnowledgeBaseConfiguration. Deleting an agent still auto-cleans its rows
        // (Agent cascade in AgentConfiguration); a normal (soft) KnowledgeBase delete leaves this
        // row untouched (FR-049 re-validates access per-execution regardless, so a
        // soft-deleted-but-still-configured KB simply drops out of the caller's authorized set at
        // execution time).
        builder.HasOne<KnowledgeBase>()
            .WithMany()
            .HasForeignKey(k => k.KnowledgeBaseId)
            .OnDelete(DeleteBehavior.Restrict);

        // The Agent <-> AgentKnowledgeBase relationship is configured from AgentConfiguration.
    }
}
