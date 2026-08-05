using AskLucy.Domain.Documents;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

/// <summary>EF Core mapping for <see cref="Document"/> — persistence mapping lives entirely here, never as attributes on the Domain entity (constitution §3).</summary>
public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.OwnerId).IsRequired();
        builder.Property(d => d.FileName).IsRequired().HasMaxLength(260);
        builder.Property(d => d.FileType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.SizeBytes).IsRequired();
        builder.Property(d => d.ProcessingStatus).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(d => d.CreatedBy).IsRequired();
        builder.Property(d => d.RowVersion).IsRowVersion();

        builder.HasQueryFilter(d => d.DeletedAtUtc == null);

        builder.HasIndex(d => d.OwnerId);
        builder.HasIndex(d => d.FolderId);
        builder.HasIndex(d => d.ProcessingStatus);
        builder.HasIndex(d => d.ArchivedAtUtc);
        // No DB-enforced FK for CurrentVersionId -> DocumentVersion: DocumentVersion.DocumentId
        // already has a real FK back to Document, so a second, opposite-direction FK here would
        // create a circular constraint SQL Server cannot satisfy on insert (neither row can be
        // written first). CurrentVersionId is validated at the Application layer instead.
        builder.HasIndex(d => d.CurrentVersionId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(d => d.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<DocumentFolder>()
            .WithMany()
            .HasForeignKey(d => d.FolderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Many-to-many, implicit EF Core join table (no mapped Domain type, data-model.md) —
        // tags are shared across a user's documents, unlike KnowledgeBaseTag's per-instance model.
        builder.HasMany(d => d.Tags)
            .WithMany()
            .UsingEntity(j => j.ToTable("DocumentTagAssignments"));
        builder.Navigation(d => d.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
