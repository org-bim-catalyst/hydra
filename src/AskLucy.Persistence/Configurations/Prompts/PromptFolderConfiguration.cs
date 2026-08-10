using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Prompts;

/// <summary>EF Core mapping for <see cref="PromptFolder"/> — mirrors <c>KnowledgeBaseFolderConfiguration</c> (research.md Decision 5).</summary>
public sealed class PromptFolderConfiguration : IEntityTypeConfiguration<PromptFolder>
{
    public void Configure(EntityTypeBuilder<PromptFolder> builder)
    {
        builder.ToTable("PromptFolders");

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

        // Self-referencing parent/child — Restrict, not Cascade: a folder move/delete is
        // orchestrated explicitly by the Application layer (cycle check, orphan-on-delete),
        // never an implicit cascading side effect of a sibling row's deletion.
        builder.HasOne<PromptFolder>()
            .WithMany()
            .HasForeignKey(f => f.ParentFolderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
