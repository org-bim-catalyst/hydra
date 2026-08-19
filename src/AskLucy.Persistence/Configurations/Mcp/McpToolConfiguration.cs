using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Mcp;

/// <summary>EF Core mapping for <see cref="McpTool"/> (spec.md FR-019-FR-026, research.md Decision 3 — <see cref="McpTool.NamespacedName"/> is the string key <c>AgentTool.ToolName</c>/<c>AgentToolCall.ToolName</c>/<c>AgentPolicy.ToolName</c> reference, unique and indexed).</summary>
public sealed class McpToolConfiguration : IEntityTypeConfiguration<McpTool>
{
    public void Configure(EntityTypeBuilder<McpTool> builder)
    {
        builder.ToTable("McpTools");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        // Bounded to keep the unique nvarchar index below SQL Server's 900-byte key limit.
        builder.Property(t => t.NamespacedName).IsRequired().HasMaxLength(400);
        builder.Property(t => t.ToolName).IsRequired().HasMaxLength(200);
        builder.Property(t => t.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).IsRequired().HasMaxLength(2000);
        builder.Property(t => t.InputSchemaJson).IsRequired();
        builder.Property(t => t.OutputSchemaJson).IsRequired();
        builder.Property(t => t.DeclaredCapabilitiesJson);
        builder.Property(t => t.ServerDeclaredRiskLevel).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.EffectiveRiskLevel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.RequiredPermissionsJson).IsRequired();
        builder.Property(t => t.ActivationStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.ActivatedByUserId);
        builder.Property(t => t.Version).HasMaxLength(100);
        builder.Property(t => t.IsAvailable).IsRequired();

        builder.Property(t => t.CreatedBy).IsRequired();
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasQueryFilter(t => t.DeletedAtUtc == null);

        builder.HasIndex(t => t.NamespacedName).IsUnique();
        builder.HasIndex(t => t.McpServerId);
        builder.HasIndex(t => new { t.McpServerId, t.ActivationStatus, t.IsAvailable });

        builder.HasOne<McpServer>()
            .WithMany()
            .HasForeignKey(t => t.McpServerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<McpCapabilitySnapshot>()
            .WithMany()
            .HasForeignKey(t => t.McpCapabilitySnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
