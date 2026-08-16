using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowExecutionUsage"/> — one-to-one with <see cref="WorkflowExecution"/>.</summary>
public sealed class WorkflowExecutionUsageConfiguration : IEntityTypeConfiguration<WorkflowExecutionUsage>
{
    public void Configure(EntityTypeBuilder<WorkflowExecutionUsage> builder)
    {
        builder.ToTable("WorkflowExecutionUsages");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.InputTokenCount);
        builder.Property(u => u.OutputTokenCount);
        builder.Property(u => u.ReasoningTokenCount);
        builder.Property(u => u.ToolCallCount).IsRequired();

        builder.Property(u => u.CreatedBy);
        builder.Property(u => u.RowVersion).IsRowVersion();

        builder.HasIndex(u => u.WorkflowExecutionId).IsUnique();
    }
}
