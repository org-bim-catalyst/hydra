using AskLucy.Domain.Chats;
using AskLucy.Domain.Projects;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Persistence.Configurations.Memory;

/// <summary>
/// EF Core mapping for <see cref="MemoryEntity"/>. <c>Content</c>'s encryption converter is applied
/// in <c>AskLucyDbContext.OnModelCreating</c>, not here — it needs the DI-injected
/// <c>IAiCredentialProtector</c> instance, which only the DbContext constructor has access to
/// (research.md Decision 12).
/// </summary>
public sealed class MemoryConfiguration : IEntityTypeConfiguration<MemoryEntity>
{
    public void Configure(EntityTypeBuilder<MemoryEntity> builder)
    {
        builder.ToTable("Memories");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.UserId).IsRequired();
        builder.Property(m => m.Category).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.State).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.IsSensitive).IsRequired().HasDefaultValue(false);
        builder.Property(m => m.SourceType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(m => m.Importance).HasPrecision(3, 2).IsRequired();
        builder.Property(m => m.Confidence).HasPrecision(3, 2).IsRequired();
        builder.Property(m => m.LastReinforcedAtUtc).IsRequired();
        builder.Property(m => m.FrequencyCount).IsRequired();

        builder.Property(m => m.CreatedBy).IsRequired();
        builder.Property(m => m.RowVersion).IsRowVersion();

        builder.HasQueryFilter(m => m.DeletedAtUtc == null);

        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => new { m.UserId, m.ProjectId });
        builder.HasIndex(m => new { m.UserId, m.State });
        builder.HasIndex(m => new { m.UserId, m.Category });
        builder.HasIndex(m => m.SourceConversationId);

        // Cascades directly from ApplicationUser (not routed through Project) so that account
        // deletion (spec.md FR-026, research.md Decision 19) removes every Memory row in one
        // referential-integrity step — the same mechanism UserChatConfiguration already relies on
        // for its own account-deletion cleanup, discovered during /speckit-implement to be a
        // simpler, more robust replacement for the custom event-handler Decision 19 originally
        // proposed (no existing domain-event dispatch infrastructure exists in this codebase).
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict (not Cascade) — Project already cascades from ApplicationUser directly, so a
        // second cascade path from ApplicationUser through Project to Memory would trip SQL
        // Server's "multiple cascade paths" schema validation. Within a single account-deletion
        // cascade both rows are removed together regardless (SQL Server's cascade engine treats
        // co-cascading rows as already-deleted for Restrict-constraint purposes); Restrict still
        // correctly blocks an out-of-band attempt to delete a Project on its own while memories
        // reference it.
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // NoAction (not SetNull) — SetNull still counts as a cascading action for SQL Server's
        // "multiple cascade paths" validation, and a second path already exists from
        // ApplicationUser to Memories via the direct UserId cascade above, so SetNull here fails
        // migration deployment ("Introducing FOREIGN KEY constraint ... may cause cycles or
        // multiple cascade paths"). KNOWN GAP: PurgeUserChatCommandHandler's hard delete of a
        // UserChat will now throw a raw FK violation if any Memory still references it via
        // SourceConversationId, instead of the previously-intended silent null-out — no
        // application-level cleanup has been added for this (left as follow-up work).
        builder.HasOne<UserChat>()
            .WithMany()
            .HasForeignKey(m => m.SourceConversationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
