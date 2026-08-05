using AskLucy.Domain.Chats;
using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Retrieval;

/// <summary>Append-only (data-model.md).</summary>
public sealed class RetrievalHistoryConfiguration : IEntityTypeConfiguration<RetrievalHistory>
{
    public void Configure(EntityTypeBuilder<RetrievalHistory> builder)
    {
        builder.ToTable("RetrievalHistories");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();

        builder.Property(h => h.UserId).IsRequired();
        builder.Property(h => h.Query).IsRequired().HasMaxLength(4000);
        builder.Property(h => h.SearchMode).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(h => h.KnowledgeBaseIdsSearchedJson).IsRequired();
        builder.Property(h => h.TopK).IsRequired();
        builder.Property(h => h.SimilarityThreshold).IsRequired().HasPrecision(5, 4);
        builder.Property(h => h.MaxContextTokens).IsRequired();
        builder.Property(h => h.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(h => h.DurationMs).IsRequired();
        builder.Property(h => h.ResultCount).IsRequired();

        builder.Property(h => h.CreatedBy).IsRequired();
        builder.Property(h => h.RowVersion).IsRowVersion();

        builder.HasIndex(h => h.UserChatId);
        builder.HasIndex(h => h.UserId);
        builder.HasIndex(h => h.CreatedAtUtc);

        builder.HasOne<UserChat>()
            .WithMany()
            .HasForeignKey(h => h.UserChatId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(h => h.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
