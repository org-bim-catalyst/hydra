using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Projects;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="UserChat"/> — persistence mapping lives entirely here,
/// never as attributes on the Domain entity (constitution &#167;3). Migrates the legacy
/// int-keyed table onto the standard entity conventions (data-model.md, research.md Topic 5).
/// </summary>
public sealed class UserChatConfiguration : IEntityTypeConfiguration<UserChat>
{
    public void Configure(EntityTypeBuilder<UserChat> builder)
    {
        builder.ToTable("UserChats");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Title).IsRequired().HasMaxLength(200);
        builder.Property(c => c.SessionId).HasMaxLength(100);
        builder.Property(c => c.UserId).IsRequired();
        builder.Property(c => c.IsTitleManuallySet).IsRequired().HasDefaultValue(false);
        builder.Property(c => c.IsFavorite).IsRequired().HasDefaultValue(false);

        // specs/005-multi-provider-ai-engine (FR-008/FR-009/FR-014): the conversation's
        // *current* selection — a live FK, unlike Message.Provider/Model's historical
        // string snapshot (see MessageConfiguration).
        builder.Property(c => c.ProviderId);
        builder.Property(c => c.ModelId);
        builder.Property(c => c.GenerationParametersJson);

        // Retrieval settings overrides (spec.md FR-020, FR-023, FR-024, research.md Decision 10).
        builder.Property(c => c.RetrievalSearchMode).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.RetrievalTopK);
        builder.Property(c => c.RetrievalSimilarityThreshold).HasPrecision(5, 4);
        builder.Property(c => c.RetrievalMaxContextTokens);

        // AI Memory System (specs/018-ai-memory-system, research.md Decision 1/6).
        builder.Property(c => c.ProjectId);
        builder.Property(c => c.LastMemoryAnalyzedAtUtc);

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasQueryFilter(c => c.DeletedAtUtc == null);

        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.CreatedAtUtc);
        builder.HasIndex(c => c.ArchivedAtUtc);
        builder.HasIndex(c => c.PinnedAtUtc);
        builder.HasIndex(c => c.IsFavorite);
        builder.HasIndex(c => c.ProviderId);
        builder.HasIndex(c => c.ModelId);
        builder.HasIndex(c => c.ProjectId);

        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.UserChats)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AIProvider>()
            .WithMany()
            .HasForeignKey(c => c.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AIModel>()
            .WithMany()
            .HasForeignKey(c => c.ModelId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict — Project already cascades from ApplicationUser directly (ProjectConfiguration);
        // a second cascade path from ApplicationUser through Project to UserChat would trip SQL
        // Server's multiple-cascade-paths validation, mirroring MemoryConfiguration's identical
        // reasoning for Memory.ProjectId.
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // specs/037-location-query-resolution — four nullable columns on the existing UserChats
        // table; null = no location confirmed yet for this chat (no data backfill on migration).
        builder.OwnsOne(c => c.ActiveLocation, owned =>
        {
            owned.Property(a => a.Latitude).HasColumnName("ActiveLocationLatitude");
            owned.Property(a => a.Longitude).HasColumnName("ActiveLocationLongitude");
            owned.Property(a => a.LocationName).HasColumnName("ActiveLocationName").HasMaxLength(500);
            owned.Property(a => a.Confidence).HasColumnName("ActiveLocationConfidence");
        });
    }
}
