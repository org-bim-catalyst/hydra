using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Retrieval;

public sealed class ChunkStatisticsConfiguration : IEntityTypeConfiguration<ChunkStatistics>
{
    public void Configure(EntityTypeBuilder<ChunkStatistics> builder)
    {
        builder.ToTable("ChunkStatistics");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.TotalChunks).IsRequired();
        builder.Property(s => s.TotalEmbeddings).IsRequired();
        builder.Property(s => s.StorageBytes).IsRequired();
        builder.Property(s => s.ComputedAtUtc).IsRequired();

        builder.Property(s => s.CreatedBy).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => s.KnowledgeBaseId).IsUnique();

        builder.HasOne<KnowledgeBase>()
            .WithMany()
            .HasForeignKey(s => s.KnowledgeBaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
