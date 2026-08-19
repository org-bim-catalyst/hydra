using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowVersion"/> — immutable, append-only.</summary>
public sealed class WorkflowVersionConfiguration : IEntityTypeConfiguration<WorkflowVersion>
{
    public void Configure(EntityTypeBuilder<WorkflowVersion> builder)
    {
        builder.ToTable("WorkflowVersions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.VersionNumber).IsRequired();
        builder.Property(v => v.InputsSchemaJson).IsRequired();
        builder.Property(v => v.OutputsSchemaJson).IsRequired();
        builder.Property(v => v.ErrorPolicyJson).IsRequired();
        builder.Property(v => v.ExecutionPolicyJson).IsRequired();
        builder.Property(v => v.SecurityPolicyJson).IsRequired();
        builder.Property(v => v.PublishedBy).IsRequired();
        builder.Property(v => v.ChangeDescription).HasMaxLength(500);

        builder.Property(v => v.CreatedBy).IsRequired();
        builder.Property(v => v.RowVersion).IsRowVersion();

        builder.HasIndex(v => new { v.WorkflowId, v.VersionNumber }).IsUnique();

        // Nodes/Connections/Variables are children of this version — reachable only via these
        // navigations (backed by private fields), mirrors AgentExecution.Steps. Cascade, since
        // child rows have no independent meaning outside their version.
        builder.HasMany(v => v.Nodes)
            .WithOne()
            .HasForeignKey(n => n.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Nodes).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(v => v.Connections)
            .WithOne()
            .HasForeignKey(c => c.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Connections).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(v => v.Variables)
            .WithOne()
            .HasForeignKey(vr => vr.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Variables).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The Workflow <-> WorkflowVersion relationship itself is configured from
        // WorkflowConfiguration (HasMany(w => w.Versions)), not here — mirrors Agent/AgentVersion's
        // single-side configuration convention.
    }
}
