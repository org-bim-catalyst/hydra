using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowExecutionNode"/> — child of <see cref="WorkflowExecution"/>.</summary>
public sealed class WorkflowExecutionNodeConfiguration : IEntityTypeConfiguration<WorkflowExecutionNode>
{
    public void Configure(EntityTypeBuilder<WorkflowExecutionNode> builder)
    {
        builder.ToTable("WorkflowExecutionNodes");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();

        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(n => n.InputJson);
        builder.Property(n => n.OutputJson);
        builder.Property(n => n.RetryCount).IsRequired();
        builder.Property(n => n.ResolvedIdempotencyKey).HasMaxLength(500);
        builder.Property(n => n.SkippedReason).HasMaxLength(500);

        builder.Property(n => n.CreatedBy);
        builder.Property(n => n.RowVersion).IsRowVersion();

        builder.HasIndex(n => new { n.WorkflowExecutionId, n.WorkflowNodeId }).IsUnique();
        builder.HasIndex(n => n.Status);

        // Restrict, not Cascade — avoids a multiple-cascade-paths conflict with
        // WorkflowVersion -> WorkflowNode's own Cascade (mirrors this codebase's prior
        // AgentApprovals/AgentKnowledgeBases FK fixes for the identical SQL Server constraint).
        builder.HasOne<WorkflowNode>()
            .WithMany()
            .HasForeignKey(n => n.WorkflowNodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
