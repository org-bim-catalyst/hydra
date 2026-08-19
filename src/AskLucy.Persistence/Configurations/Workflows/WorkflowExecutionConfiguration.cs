using AskLucy.Domain.Workflows;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowExecution"/> — aggregate root for the runtime bounded context; never hard-deleted (FR-052 audit trail), so no query filter on <c>DeletedAtUtc</c> is needed (it is never set for this entity).</summary>
public sealed class WorkflowExecutionConfiguration : IEntityTypeConfiguration<WorkflowExecution>
{
    public void Configure(EntityTypeBuilder<WorkflowExecution> builder)
    {
        builder.ToTable("WorkflowExecutions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.RunByUserId).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.TriggerType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.TriggeringEventReferenceJson);
        builder.Property(e => e.InputsJson).IsRequired();
        builder.Property(e => e.VariablesJson).IsRequired();
        builder.Property(e => e.FinalOutputJson);
        builder.Property(e => e.TerminationReason).HasMaxLength(2000);

        builder.Property(e => e.CreatedBy).IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => e.RunByUserId);
        builder.HasIndex(e => e.WorkflowId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => new { e.RunByUserId, e.Status });

        builder.HasOne<Workflow>()
            .WithMany()
            .HasForeignKey(e => e.WorkflowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WorkflowVersion>()
            .WithMany()
            .HasForeignKey(e => e.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.RunByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Nodes/Events/Approvals/Errors are children of this aggregate — reachable only via these
        // navigations (backed by private fields), mirrors AgentExecution.Steps. Cascade, since
        // child rows have no independent meaning outside their execution (data-model.md).
        builder.HasMany(e => e.Nodes)
            .WithOne()
            .HasForeignKey(n => n.WorkflowExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Nodes).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(e => e.Events)
            .WithOne()
            .HasForeignKey(ev => ev.WorkflowExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Events).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(e => e.Approvals)
            .WithOne()
            .HasForeignKey(a => a.WorkflowExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Approvals).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(e => e.Errors)
            .WithOne()
            .HasForeignKey(er => er.WorkflowExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Errors).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(e => e.Usage)
            .WithOne()
            .HasForeignKey<WorkflowExecutionUsage>(u => u.WorkflowExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Cost)
            .WithOne()
            .HasForeignKey<WorkflowExecutionCost>(c => c.WorkflowExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
