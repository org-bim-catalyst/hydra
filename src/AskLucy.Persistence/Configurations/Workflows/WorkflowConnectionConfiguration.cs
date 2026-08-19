using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowConnection"/> — child of <see cref="WorkflowVersion"/>, immutable once created.</summary>
public sealed class WorkflowConnectionConfiguration : IEntityTypeConfiguration<WorkflowConnection>
{
    public void Configure(EntityTypeBuilder<WorkflowConnection> builder)
    {
        builder.ToTable("WorkflowConnections");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.SourceNodeId).IsRequired();
        builder.Property(c => c.TargetNodeId).IsRequired();
        builder.Property(c => c.BranchLabel).HasMaxLength(100);
        builder.Property(c => c.TypeContract).HasMaxLength(200);

        builder.Property(c => c.CreatedBy);
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasIndex(c => c.SourceNodeId);
        builder.HasIndex(c => c.TargetNodeId);

        // No FK to WorkflowNode here — SourceNodeId/TargetNodeId are validated at construction
        // time (Workflow.Publish) against the same version's node set; a real FK constraint would
        // require SQL Server to support multiple cascade paths through WorkflowVersion, which it
        // does not (mirrors the platform's existing multiple-cascade-paths precedent).
    }
}
