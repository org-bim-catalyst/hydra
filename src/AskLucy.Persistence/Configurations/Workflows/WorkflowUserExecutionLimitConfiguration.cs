using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowUserExecutionLimit"/> — administrator-managed, independent lifecycle.</summary>
public sealed class WorkflowUserExecutionLimitConfiguration : IEntityTypeConfiguration<WorkflowUserExecutionLimit>
{
    public void Configure(EntityTypeBuilder<WorkflowUserExecutionLimit> builder)
    {
        builder.ToTable("WorkflowUserExecutionLimits");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.UserId).IsRequired();
        builder.Property(l => l.MaxConcurrentExecutions).IsRequired();
        builder.Property(l => l.SetByUserId).IsRequired();

        builder.Property(l => l.CreatedBy).IsRequired();
        builder.Property(l => l.RowVersion).IsRowVersion();

        builder.HasIndex(l => l.UserId).IsUnique();
    }
}
