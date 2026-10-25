using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentLanguageConfiguration : IEntityTypeConfiguration<DocumentLanguage>
{
    public void Configure(EntityTypeBuilder<DocumentLanguage> builder)
    {
        builder.ToTable("DocumentLanguages");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(l => l.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.ConfidenceScore).HasColumnType("decimal(5,4)").IsRequired();

        builder.Property(l => l.CreatedBy).IsRequired();
        builder.Property(l => l.RowVersion).IsRowVersion();

        // specs/015 US5 reprocessing replaces a document's language set with
        // DocumentRepository.RemoveLanguages, which is a RemoveRange — and AuditSaveChangesInterceptor
        // turns every BaseEntity delete into a soft delete. Without this filter those rows stay
        // readable, so GetLanguagesByDocumentIdAsync keeps returning them and the set grows on every
        // reprocess. Every other soft-deletable entity in this project declares the same filter.
        builder.HasQueryFilter(l => l.DeletedAtUtc == null);

        builder.HasIndex(l => l.DocumentId);

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(l => l.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
