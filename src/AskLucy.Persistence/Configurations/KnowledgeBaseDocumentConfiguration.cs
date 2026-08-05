using AskLucy.Domain.Documents;
using AskLucy.Domain.KnowledgeBases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

public sealed class KnowledgeBaseDocumentConfiguration : IEntityTypeConfiguration<KnowledgeBaseDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseDocument> builder)
    {
        builder.ToTable("KnowledgeBaseDocuments");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.FileName).IsRequired().HasMaxLength(260);
        builder.Property(d => d.StoredFileName).IsRequired().HasMaxLength(300);
        builder.Property(d => d.ContentType).IsRequired().HasMaxLength(200);
        builder.Property(d => d.SizeBytes).IsRequired();
        builder.Property(d => d.ProcessingStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.UploadedAtUtc).IsRequired();

        builder.Property(d => d.CreatedBy).IsRequired();
        builder.Property(d => d.RowVersion).IsRowVersion();

        builder.HasQueryFilter(d => d.DeletedAtUtc == null);

        builder.HasIndex(d => d.KnowledgeBaseId);
        builder.HasIndex(d => d.FolderId);
        builder.HasIndex(d => d.DocumentId);

        builder.HasOne<KnowledgeBase>()
            .WithMany()
            .HasForeignKey(d => d.KnowledgeBaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<KnowledgeBaseFolder>()
            .WithMany()
            .HasForeignKey(d => d.FolderId)
            .OnDelete(DeleteBehavior.Restrict);

        // research.md Decision 2 — nullable link into the Document Intelligence Pipeline,
        // populated lazily by the RAG indexing pipeline.
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(d => d.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
