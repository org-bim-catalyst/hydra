using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

/// <summary>EF Core mapping for <see cref="AgentVersion"/> — immutable, append-only; cascades from its owning <see cref="Agent"/> configuration is Restrict, not Cascade (data-model.md Delete Behavior), so a soft-deleted agent's published versions are retained for audit.</summary>
public sealed class AgentVersionConfiguration : IEntityTypeConfiguration<AgentVersion>
{
    public void Configure(EntityTypeBuilder<AgentVersion> builder)
    {
        builder.ToTable("AgentVersions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.VersionNumber).IsRequired();
        builder.Property(v => v.ModelProviderId).IsRequired();
        builder.Property(v => v.ModelId).IsRequired();
        builder.Property(v => v.OutputFormat).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(v => v.ToolsSnapshotJson).IsRequired();
        builder.Property(v => v.KnowledgeBasesSnapshotJson).IsRequired();
        builder.Property(v => v.MemoryPolicySnapshotJson);
        builder.Property(v => v.ChangeDescription).HasMaxLength(500);

        builder.OwnsOne(v => v.Instructions, instructions =>
        {
            instructions.Property(i => i.SystemInstructions).HasColumnName("SystemInstructions");
            instructions.Property(i => i.Objectives).HasColumnName("Objectives");
            instructions.Property(i => i.Constraints).HasColumnName("Constraints");
            instructions.Property(i => i.BehavioralRules).HasColumnName("BehavioralRules");
            instructions.Property(i => i.OutputRequirements).HasColumnName("OutputRequirements");
            instructions.Property(i => i.ToolUsageRules).HasColumnName("ToolUsageRules");
            instructions.Property(i => i.SafetyRules).HasColumnName("SafetyRules");
        });
        builder.Navigation(v => v.Instructions).IsRequired();

        builder.OwnsOne(v => v.ExecutionPolicy, policy =>
        {
            policy.Property(p => p.MaxSteps).HasColumnName("MaxSteps");
            policy.Property(p => p.MaxExecutionDurationSeconds).HasColumnName("MaxExecutionDurationSeconds");
            policy.Property(p => p.MaxTokens).HasColumnName("MaxTokens");
            policy.Property(p => p.MaxCost).HasColumnName("MaxCost").HasColumnType("decimal(10,4)");
            policy.Property(p => p.MaxToolCalls).HasColumnName("MaxToolCalls");
            policy.Property(p => p.MaxRetries).HasColumnName("MaxRetries");
        });
        builder.Navigation(v => v.ExecutionPolicy).IsRequired();

        builder.Property(v => v.CreatedBy).IsRequired();
        builder.Property(v => v.RowVersion).IsRowVersion();

        builder.HasIndex(v => new { v.AgentId, v.VersionNumber }).IsUnique();

        // The Agent <-> AgentVersion relationship itself is configured from AgentConfiguration
        // (HasMany(a => a.Versions)), not here — mirrors Prompt/PromptVersion's single-side
        // configuration convention.
    }
}
