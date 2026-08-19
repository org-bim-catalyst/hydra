using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

public sealed class AgentExecutionStepConfiguration : IEntityTypeConfiguration<AgentExecutionStep>
{
    public void Configure(EntityTypeBuilder<AgentExecutionStep> builder)
    {
        builder.ToTable("AgentExecutionSteps");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.StepIndex).IsRequired();
        builder.Property(s => s.Description).IsRequired().HasMaxLength(2000);
        builder.Property(s => s.StepType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(s => s.InputJson);
        builder.Property(s => s.OutputJson);
        // Widened from 100 (spec 021-mcp-integration, research.md Decision 3) — an MCP tool's
        // namespaced identifier ("mcp:{serverId}:{toolName}") can exceed the original bound sized
        // only for short native tool class names; 400 matches McpTool.NamespacedName's own bound.
        builder.Property(s => s.ToolName).HasMaxLength(400);

        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => new { s.AgentExecutionId, s.StepIndex }).IsUnique();

        // The AgentExecution <-> AgentExecutionStep relationship is configured from
        // AgentExecutionConfiguration. DependsOnStepId/ErrorId are soft references (no FK
        // constraint) — both point at sibling rows within the same aggregate that already share
        // AgentExecutionId, and a self-referencing/cross-table FK here would only add migration
        // complexity without a real integrity risk (both are set by the same transaction that
        // creates this row).
    }
}
