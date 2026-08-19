using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowPolicy"/> — independent lifecycle, not owned by any single <see cref="Workflow"/>.</summary>
public sealed class WorkflowPolicyConfiguration : IEntityTypeConfiguration<WorkflowPolicy>
{
    public void Configure(EntityTypeBuilder<WorkflowPolicy> builder)
    {
        builder.ToTable("WorkflowPolicies");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.WorkflowNodeType).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.UnderlyingToolName).HasMaxLength(200);
        builder.Property(p => p.ConditionsJson);
        builder.Property(p => p.CreatedByUserId).IsRequired();
        builder.Property(p => p.IsEnabled).IsRequired();

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasQueryFilter(p => p.DeletedAtUtc == null);

        builder.HasIndex(p => p.UnderlyingToolName);
        builder.HasIndex(p => p.WorkflowNodeType);
        builder.HasIndex(p => p.IsEnabled);
    }
}
