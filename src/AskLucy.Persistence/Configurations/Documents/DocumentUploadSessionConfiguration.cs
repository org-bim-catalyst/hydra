using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentUploadSessionConfiguration : IEntityTypeConfiguration<DocumentUploadSession>
{
    public void Configure(EntityTypeBuilder<DocumentUploadSession> builder)
    {
        builder.ToTable("DocumentUploadSessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.OwnerId).IsRequired();
        builder.Property(s => s.FileName).IsRequired().HasMaxLength(260);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(s => s.PendingStoredFileName).HasMaxLength(300);
        builder.Property(s => s.PendingChecksumHash).HasMaxLength(64);
        builder.Property(s => s.ExpiresAtUtc).IsRequired();

        builder.Property(s => s.CreatedBy).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => s.OwnerId);
        builder.HasIndex(s => s.ExpiresAtUtc);
        builder.HasIndex(s => new { s.TargetDocumentId, s.Status });
    }
}
