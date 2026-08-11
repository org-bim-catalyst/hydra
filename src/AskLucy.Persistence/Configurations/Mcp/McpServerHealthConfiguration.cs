using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Mcp;

/// <summary>EF Core mapping for <see cref="McpServerHealth"/> (spec.md FR-055/FR-056) — one current row per server, overwritten on every check (research.md Decision 10).</summary>
public sealed class McpServerHealthConfiguration : IEntityTypeConfiguration<McpServerHealth>
{
    public void Configure(EntityTypeBuilder<McpServerHealth> builder)
    {
        builder.ToTable("McpServerHealths");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();

        builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(h => h.FailureCategory).HasConversion<string>().HasMaxLength(30);
        builder.Property(h => h.Detail).HasMaxLength(1000);
        builder.Property(h => h.CheckedAtUtc).IsRequired();
        builder.Property(h => h.ConsecutiveFailureCount).IsRequired();

        builder.Property(h => h.CreatedBy).IsRequired();
        builder.Property(h => h.RowVersion).IsRowVersion();

        builder.HasQueryFilter(h => h.DeletedAtUtc == null);

        builder.HasIndex(h => h.McpServerId).IsUnique();

        builder.HasOne<McpServer>()
            .WithMany()
            .HasForeignKey(h => h.McpServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
