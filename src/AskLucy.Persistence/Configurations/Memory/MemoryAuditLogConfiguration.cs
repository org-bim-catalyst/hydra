using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Memory;

/// <summary>Append-only, no FK on <see cref="MemoryAuditLog.MemoryId"/> (nullable) — an audit entry must survive a hard-purged memory (research.md Decision 19), mirroring <c>DocumentAuditLogConfiguration</c> exactly.</summary>
public sealed class MemoryAuditLogConfiguration : IEntityTypeConfiguration<MemoryAuditLog>
{
    public void Configure(EntityTypeBuilder<MemoryAuditLog> builder)
    {
        builder.ToTable("MemoryAuditLogs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.ActorUserId).IsRequired();
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.OccurredAtUtc).IsRequired();
        builder.Property(a => a.DetailsJson).HasMaxLength(2000);

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => new { a.UserId, a.OccurredAtUtc });
        builder.HasIndex(a => a.MemoryId);
    }
}
