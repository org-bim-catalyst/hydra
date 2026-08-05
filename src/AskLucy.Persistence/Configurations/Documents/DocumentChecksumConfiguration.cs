using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentChecksumConfiguration : IEntityTypeConfiguration<DocumentChecksum>
{
    public void Configure(EntityTypeBuilder<DocumentChecksum> builder)
    {
        builder.ToTable("DocumentChecksums");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Algorithm).IsRequired().HasMaxLength(20);
        builder.Property(c => c.Hash).IsRequired().HasMaxLength(64);

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        // Duplicate-detection lookup (FR-009) joins Document (OwnerId) -> DocumentVersion ->
        // DocumentChecksum, so this index on Hash alone (combined with the existing indexes on
        // Document.OwnerId/DocumentVersion.DocumentId) is sufficient — no OwnerId column exists
        // on this table itself (data-model.md).
        builder.HasIndex(c => c.Hash);
    }
}
