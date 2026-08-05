using AskLucy.Domain.Chats;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Retrieval;

public sealed class ConversationKnowledgeBaseConfiguration : IEntityTypeConfiguration<ConversationKnowledgeBase>
{
    public void Configure(EntityTypeBuilder<ConversationKnowledgeBase> builder)
    {
        builder.ToTable("ConversationKnowledgeBases");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.AttachedAtUtc).IsRequired();

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasQueryFilter(c => c.DeletedAtUtc == null);

        // FR-035 — unique attachment per (conversation, knowledge base); re-attaching an
        // already-attached knowledge base is a no-op at the Application layer, not a duplicate row.
        builder.HasIndex(c => new { c.UserChatId, c.KnowledgeBaseId }).IsUnique();

        builder.HasOne<UserChat>()
            .WithMany()
            .HasForeignKey(c => c.UserChatId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade, on the KnowledgeBase side — SQL Server rejects two cascade paths
        // converging on the same join table ("may cause cycles or multiple cascade paths",
        // discovered applying this migration during /speckit-implement). Deleting a conversation
        // still auto-cleans its attachment rows (UserChat cascade above); a knowledge base's rare
        // hard-delete/purge path already explicitly cleans up its related data (specs/014) and
        // does the same here.
        builder.HasOne<KnowledgeBase>()
            .WithMany()
            .HasForeignKey(c => c.KnowledgeBaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
