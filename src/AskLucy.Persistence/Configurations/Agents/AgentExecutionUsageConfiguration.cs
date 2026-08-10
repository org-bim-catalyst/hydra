using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

public sealed class AgentExecutionUsageConfiguration : IEntityTypeConfiguration<AgentExecutionUsage>
{
    public void Configure(EntityTypeBuilder<AgentExecutionUsage> builder)
    {
        builder.ToTable("AgentExecutionUsages");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.ToolCallCount).IsRequired();
        builder.Property(u => u.StepCount).IsRequired();

        builder.Property(u => u.RowVersion).IsRowVersion();

        builder.HasIndex(u => u.AgentExecutionId).IsUnique();

        // The AgentExecution <-> AgentExecutionUsage 1:1 relationship is configured from
        // AgentExecutionConfiguration.
    }
}
