using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("DocumentVersions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.StoredFileName).IsRequired().HasMaxLength(300);
        builder.Property(v => v.OriginalFileName).IsRequired().HasMaxLength(260);
        builder.Property(v => v.SizeBytes).IsRequired();
        builder.Property(v => v.ExtractedText).HasColumnType("nvarchar(max)");
        builder.Property(v => v.ExtractedStructureJson).HasColumnType("nvarchar(max)");
        builder.Property(v => v.OcrTextRaw).HasColumnType("nvarchar(max)");

        builder.Property(v => v.CreatedBy).IsRequired();
        builder.Property(v => v.RowVersion).IsRowVersion();

        builder.HasIndex(v => v.DocumentId);
        builder.HasIndex(v => v.ChecksumId);

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(v => v.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<DocumentChecksum>()
            .WithMany()
            .HasForeignKey(v => v.ChecksumId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
