using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentPreviewConfiguration : IEntityTypeConfiguration<DocumentPreview>
{
    public void Configure(EntityTypeBuilder<DocumentPreview> builder)
    {
        builder.ToTable("DocumentPreviews");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.PreviewType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.StoredFileName).HasMaxLength(300);

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.DocumentVersionId);

        builder.HasOne<DocumentVersion>()
            .WithMany()
            .HasForeignKey(p => p.DocumentVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
