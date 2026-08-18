using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Persistence.Configurations.Memory;

public sealed class MemoryConflictConfiguration : IEntityTypeConfiguration<MemoryConflict>
{
    public void Configure(EntityTypeBuilder<MemoryConflict> builder)
    {
        builder.ToTable("MemoryConflicts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.ConflictType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(c => c.ResolutionStatus).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.DetectedAtUtc).IsRequired();

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasIndex(c => c.ExistingMemoryId);
        builder.HasIndex(c => new { c.ExistingMemoryId, c.ResolutionStatus });

        // Cascade on ExistingMemoryId only. Two CASCADE FKs from the same table to the same parent
        // table (ExistingMemoryId and NewMemoryId both -> Memory) trips SQL Server's "may cause
        // cycles or multiple cascade paths" schema-creation error — the same real, verified failure
        // ConversationKnowledgeBaseConfiguration already documents for its own dual-FK case.
        // Restrict on NewMemoryId still lets an account-deletion purge succeed: both referenced
        // Memory rows are removed together in the same cascading operation rooted at
        // ApplicationUser (MemoryConfiguration's own Cascade from ApplicationUser), and SQL
        // Server's cascade engine treats co-cascading rows as already-deleted for Restrict-
        // constraint purposes within one statement — only an out-of-band standalone delete of a
        // referenced memory would be blocked, which is the desired behavior anyway.
        builder.HasOne<MemoryEntity>()
            .WithMany()
            .HasForeignKey(c => c.ExistingMemoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<MemoryEntity>()
            .WithMany()
            .HasForeignKey(c => c.NewMemoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
