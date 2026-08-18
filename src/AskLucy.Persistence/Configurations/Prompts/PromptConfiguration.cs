using AskLucy.Domain.Prompts;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Prompts;

/// <summary>EF Core mapping for <see cref="Prompt"/> — persistence mapping lives entirely here, never as attributes on the Domain entity (constitution &#167;3).</summary>
public sealed class PromptConfiguration : IEntityTypeConfiguration<Prompt>
{
    public void Configure(EntityTypeBuilder<Prompt> builder)
    {
        builder.ToTable("Prompts");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.OwnerId).IsRequired();
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.PromptType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(p => p.SystemInstructions);
        builder.Property(p => p.DeveloperInstructions);
        builder.Property(p => p.UserInstructions).IsRequired();
        builder.Property(p => p.ContextText);
        builder.Property(p => p.ExamplesText);
        builder.Property(p => p.OutputInstructions);
        builder.Property(p => p.Constraints);

        builder.Property(p => p.IsFavorite).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.IsPinned).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.PreferredModelKey).HasMaxLength(100);
        builder.Property(p => p.CurrentVersionNumber).IsRequired();

        // Required-capability flags (data-model.md) — flat columns, same shape AIModel itself
        // uses for its own capabilities (research.md Decision 3/Prompt.cs doc comment), not an
        // owned type.
        builder.OwnsOne(p => p.RequiredCapabilities, capabilities =>
        {
            capabilities.Property(c => c.RequiresStreaming).HasColumnName("RequiresStreaming").IsRequired();
            capabilities.Property(c => c.RequiresVision).HasColumnName("RequiresVision").IsRequired();
            capabilities.Property(c => c.RequiresFunctionCalling).HasColumnName("RequiresFunctionCalling").IsRequired();
            capabilities.Property(c => c.RequiresJsonMode).HasColumnName("RequiresJsonMode").IsRequired();
            capabilities.Property(c => c.RequiresReasoning).HasColumnName("RequiresReasoning").IsRequired();
            capabilities.Property(c => c.RequiresEmbeddings).HasColumnName("RequiresEmbeddings").IsRequired();
            capabilities.Property(c => c.RequiresImageInput).HasColumnName("RequiresImageInput").IsRequired();
            capabilities.Property(c => c.RequiresImageOutput).HasColumnName("RequiresImageOutput").IsRequired();
            capabilities.Property(c => c.RequiresAudio).HasColumnName("RequiresAudio").IsRequired();
        });

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasQueryFilter(p => p.DeletedAtUtc == null);

        // Name uniqueness per owner, case-insensitive (research.md Decision 7, FR-006) — defense
        // in depth alongside the Application-layer pre-check. SQL Server's default collation for
        // this database is already case-insensitive, so a plain composite index enforces this
        // without a computed lower-invariant column.
        builder.HasIndex(p => new { p.OwnerId, p.Name }).IsUnique().HasFilter("[DeletedAtUtc] IS NULL");

        builder.HasIndex(p => p.FolderId);
        builder.HasIndex(p => p.CategoryId);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.IsFavorite);
        builder.HasIndex(p => p.IsPinned);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PromptFolder>()
            .WithMany()
            .HasForeignKey(p => p.FolderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<PromptCategory>()
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Tags are children of this aggregate — reachable only via this navigation (backed by
        // the private field), never their own DbSet's aggregate ownership, mirrors
        // KnowledgeBase.Tags. ListTagsQuery still queries the PromptTags DbSet directly for an
        // owner-scoped distinct list (research.md Decision 6).
        builder.HasMany(p => p.Tags)
            .WithOne()
            .HasForeignKey(t => t.PromptId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Versions are children of this aggregate — configuring the relationship from this side
        // (not PromptVersionConfiguration) is what makes `dbContext.Prompts.Add(prompt)` cascade
        // through the whole prompt+version+variables graph via reachability, mirrors Tags above.
        builder.HasMany(p => p.Versions)
            .WithOne()
            .HasForeignKey(v => v.PromptId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
