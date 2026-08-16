using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowApproval"/> — child of <see cref="WorkflowExecution"/>, never deleted (audit trail).</summary>
public sealed class WorkflowApprovalConfiguration : IEntityTypeConfiguration<WorkflowApproval>
{
    public void Configure(EntityTypeBuilder<WorkflowApproval> builder)
    {
        builder.ToTable("WorkflowApprovals");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.WorkflowExecutionNodeId).IsRequired();
        builder.Property(a => a.IntendedActionDescription).IsRequired().HasMaxLength(2000);
        builder.Property(a => a.ParametersJson);
        builder.Property(a => a.Decision).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.WasPolicyBased).IsRequired();
        builder.Property(a => a.MatchedWorkflowPolicyId);
        builder.Property(a => a.DecidedByUserId);
        builder.Property(a => a.TimeoutSeconds);

        builder.Property(a => a.CreatedBy);
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => a.WorkflowExecutionNodeId);
        builder.HasIndex(a => a.Decision);

        builder.HasOne<WorkflowPolicy>()
            .WithMany()
            .HasForeignKey(a => a.MatchedWorkflowPolicyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
