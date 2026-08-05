using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentMetadataConfiguration : IEntityTypeConfiguration<DocumentMetadata>
{
    public void Configure(EntityTypeBuilder<DocumentMetadata> builder)
    {
        builder.ToTable("DocumentMetadata");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Title).HasMaxLength(500);
        builder.Property(m => m.Author).HasMaxLength(300);
        builder.Property(m => m.Keywords).HasMaxLength(2000);
        builder.Property(m => m.Encoding).HasMaxLength(50);
        builder.Property(m => m.IsAutoExtracted).IsRequired();

        builder.Property(m => m.CreatedBy).IsRequired();
        builder.Property(m => m.RowVersion).IsRowVersion();

        builder.HasIndex(m => m.DocumentId).IsUnique();

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(m => m.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
