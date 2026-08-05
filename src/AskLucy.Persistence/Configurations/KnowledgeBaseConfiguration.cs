using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Retrieval;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="KnowledgeBase"/> — persistence mapping lives entirely here, never as attributes on the Domain entity (constitution &#167;3).</summary>
public sealed class KnowledgeBaseConfiguration : IEntityTypeConfiguration<KnowledgeBase>
{
    public void Configure(EntityTypeBuilder<KnowledgeBase> builder)
    {
        builder.ToTable("KnowledgeBases");

        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();

        builder.Property(k => k.OwnerId).IsRequired();
        builder.Property(k => k.Name).IsRequired().HasMaxLength(200);
        builder.Property(k => k.Description).HasMaxLength(2000);
        builder.Property(k => k.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(k => k.Visibility).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(k => k.Color).HasMaxLength(7);
        builder.Property(k => k.Icon).HasMaxLength(50);
        builder.Property(k => k.Notes).HasMaxLength(4000);
        builder.Property(k => k.IsFavorite).IsRequired().HasDefaultValue(false);
        builder.Property(k => k.DocumentCount).IsRequired().HasDefaultValue(0);
        builder.Property(k => k.TotalPageCount).IsRequired().HasDefaultValue(0);
        builder.Property(k => k.StorageSizeBytes).IsRequired().HasDefaultValue(0L);

        // Retrieval settings (spec.md FR-001, FR-009a, FR-014, research.md Decision 11).
        builder.Property(k => k.ChunkingStrategy).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(k => k.RequiresDataResidency).IsRequired().HasDefaultValue(false);
        builder.Property(k => k.IndexStatus).HasConversion<string>().HasMaxLength(20).IsRequired();

        // ADR-0007: the column default is SqlServer — it backfills every knowledge base that
        // existed before this change to the store its vectors are already in. The Domain
        // property's own initializer default (Pinecone) governs brand-new KnowledgeBase.Create(...)
        // calls from this point forward; the two defaults are deliberately different.
        builder.Property(k => k.VectorStoreProvider).HasConversion<string>().HasMaxLength(20).IsRequired().HasDefaultValue(VectorStoreProvider.SqlServer);

        builder.Property(k => k.CreatedBy).IsRequired();
        builder.Property(k => k.RowVersion).IsRowVersion();

        builder.HasQueryFilter(k => k.DeletedAtUtc == null);

        builder.HasIndex(k => k.OwnerId);
        builder.HasIndex(k => k.Status);
        builder.HasIndex(k => k.PinnedAtUtc);
        builder.HasIndex(k => k.IsFavorite);
        builder.HasIndex(k => k.CategoryId);
        builder.HasIndex(k => k.PurgeScheduledAtUtc);
        builder.HasIndex(k => k.CreatedAtUtc);
        builder.HasIndex(k => k.ModifiedAtUtc);
        builder.HasIndex(k => k.IndexStatus);
        builder.HasIndex(k => k.EmbeddingProviderId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(k => k.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<KnowledgeBaseCategory>()
            .WithMany()
            .HasForeignKey(k => k.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EmbeddingProvider>()
            .WithMany()
            .HasForeignKey(k => k.EmbeddingProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Tags are children of this aggregate — reachable only via this navigation (backed by
        // the private field, since it's a read-only collection), never their own DbSet
        // (constitution §5), mirrors Message.Attachments/Citations.
        builder.HasMany(k => k.Tags)
            .WithOne()
            .HasForeignKey(t => t.KnowledgeBaseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(k => k.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
