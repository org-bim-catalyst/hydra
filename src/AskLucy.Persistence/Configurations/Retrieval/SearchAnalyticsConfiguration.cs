using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Retrieval;

public sealed class SearchAnalyticsConfiguration : IEntityTypeConfiguration<SearchAnalytics>
{
    public void Configure(EntityTypeBuilder<SearchAnalytics> builder)
    {
        builder.ToTable("SearchAnalytics");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.SearchCount).IsRequired();
        builder.Property(a => a.AverageSimilarityScore).HasPrecision(5, 4);
        builder.Property(a => a.FailedSearchCount).IsRequired();
        builder.Property(a => a.EmptySearchCount).IsRequired();
        builder.Property(a => a.ComputedAtUtc).IsRequired();

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => new { a.UserId, a.KnowledgeBaseId });

        builder.HasOne<KnowledgeBase>()
            .WithMany()
            .HasForeignKey(a => a.KnowledgeBaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
