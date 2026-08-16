using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowAuditLog"/> — deliberately not hard-FK'd to <c>Workflows</c>/<c>WorkflowExecutions</c> (data-model.md), mirrors <c>AgentAuditLogConfiguration</c>.</summary>
public sealed class WorkflowAuditLogConfiguration : IEntityTypeConfiguration<WorkflowAuditLog>
{
    public void Configure(EntityTypeBuilder<WorkflowAuditLog> builder)
    {
        builder.ToTable("WorkflowAuditLogs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.ActorUserId).IsRequired();
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.DetailsJson).IsRequired();
        builder.Property(a => a.OccurredAtUtc).IsRequired();

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => a.WorkflowId);
        builder.HasIndex(a => a.WorkflowExecutionId);
        builder.HasIndex(a => a.ActorUserId);
        builder.HasIndex(a => a.Action);
    }
}
