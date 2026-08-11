using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Mcp;

/// <summary>EF Core mapping for <see cref="McpPrompt"/> (spec.md FR-041-FR-044, research.md Decision 16, clarification — read-only, re-synced on refresh).</summary>
public sealed class McpPromptConfiguration : IEntityTypeConfiguration<McpPrompt>
{
    public void Configure(EntityTypeBuilder<McpPrompt> builder)
    {
        builder.ToTable("McpPrompts");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.NamespacedName).IsRequired().HasMaxLength(400);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.ContentTemplate).IsRequired();
        builder.Property(p => p.IsAvailable).IsRequired();

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasQueryFilter(p => p.DeletedAtUtc == null);

        builder.HasIndex(p => p.NamespacedName).IsUnique();
        builder.HasIndex(p => p.McpServerId);

        builder.HasOne<McpServer>()
            .WithMany()
            .HasForeignKey(p => p.McpServerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<McpCapabilitySnapshot>()
            .WithMany()
            .HasForeignKey(p => p.McpCapabilitySnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
