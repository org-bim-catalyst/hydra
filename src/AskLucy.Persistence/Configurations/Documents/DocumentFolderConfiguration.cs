using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentFolderConfiguration : IEntityTypeConfiguration<DocumentFolder>
{
    public void Configure(EntityTypeBuilder<DocumentFolder> builder)
    {
        builder.ToTable("DocumentFolders");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.OwnerId).IsRequired();
        builder.Property(f => f.Name).IsRequired().HasMaxLength(200);
        builder.Property(f => f.Depth).IsRequired();

        builder.Property(f => f.CreatedBy).IsRequired();
        builder.Property(f => f.RowVersion).IsRowVersion();

        builder.HasQueryFilter(f => f.DeletedAtUtc == null);

        builder.HasIndex(f => f.OwnerId);
        builder.HasIndex(f => f.ParentFolderId);

        // Self-referencing; Restrict avoids SQL Server's multiple-cascade-path error and matches
        // the application-enforced "delete requires an explicit onContainedDocuments choice" rule
        // (Edge Cases) — the DB never auto-cascades a folder delete.
        builder.HasOne<DocumentFolder>()
            .WithMany()
            .HasForeignKey(f => f.ParentFolderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
