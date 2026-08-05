using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Retrieval;

/// <summary>Append-only (data-model.md).</summary>
public sealed class SearchHistoryConfiguration : IEntityTypeConfiguration<SearchHistory>
{
    public void Configure(EntityTypeBuilder<SearchHistory> builder)
    {
        builder.ToTable("SearchHistories");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();

        builder.Property(h => h.UserId).IsRequired();
        builder.Property(h => h.Query).IsRequired().HasMaxLength(4000);
        builder.Property(h => h.SearchMode).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(h => h.KnowledgeBaseIdsSearchedJson).IsRequired();
        builder.Property(h => h.ResultCount).IsRequired();

        builder.Property(h => h.CreatedBy).IsRequired();
        builder.Property(h => h.RowVersion).IsRowVersion();

        builder.HasIndex(h => h.UserId);
        builder.HasIndex(h => h.CreatedAtUtc);
    }
}
