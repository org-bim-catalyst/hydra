using AskLucy.Domain.KnowledgeBases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

public sealed class KnowledgeBaseFolderConfiguration : IEntityTypeConfiguration<KnowledgeBaseFolder>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseFolder> builder)
    {
        builder.ToTable("KnowledgeBaseFolders");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.Name).IsRequired().HasMaxLength(200);
        builder.Property(f => f.Depth).IsRequired();

        builder.Property(f => f.CreatedBy).IsRequired();
        builder.Property(f => f.RowVersion).IsRowVersion();

        builder.HasQueryFilter(f => f.DeletedAtUtc == null);

        builder.HasIndex(f => f.KnowledgeBaseId);
        builder.HasIndex(f => f.ParentFolderId);

        builder.HasOne<KnowledgeBase>()
            .WithMany()
            .HasForeignKey(f => f.KnowledgeBaseId)
            .OnDelete(DeleteBehavior.Cascade);

        // Self-referencing parent/child — Restrict, not Cascade: a folder move/delete is
        // orchestrated explicitly by the Application layer (circular-move check, non-empty
        // confirmation), never an implicit cascading side effect of a sibling row's deletion.
        builder.HasOne<KnowledgeBaseFolder>()
            .WithMany()
            .HasForeignKey(f => f.ParentFolderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
