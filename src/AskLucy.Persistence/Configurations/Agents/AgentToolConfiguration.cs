using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

public sealed class AgentToolConfiguration : IEntityTypeConfiguration<AgentTool>
{
    public void Configure(EntityTypeBuilder<AgentTool> builder)
    {
        builder.ToTable("AgentTools");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        // Widened from 100 (spec 021-mcp-integration, research.md Decision 3) — see
        // AgentExecutionStepConfiguration for the full rationale.
        builder.Property(t => t.ToolName).IsRequired().HasMaxLength(400);
        builder.Property(t => t.ConfigurationJson);

        builder.Property(t => t.CreatedBy).IsRequired();
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasIndex(t => new { t.AgentId, t.ToolName }).IsUnique();

        // The Agent <-> AgentTool relationship is configured from AgentConfiguration.
    }
}
