using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowExecutionCost"/> — one-to-one with <see cref="WorkflowExecution"/>.</summary>
public sealed class WorkflowExecutionCostConfiguration : IEntityTypeConfiguration<WorkflowExecutionCost>
{
    public void Configure(EntityTypeBuilder<WorkflowExecutionCost> builder)
    {
        builder.ToTable("WorkflowExecutionCosts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.EstimatedCost).HasColumnType("decimal(10,4)").IsRequired();
        builder.Property(c => c.CurrencyCode).IsRequired().HasMaxLength(3);

        builder.Property(c => c.CreatedBy);
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasIndex(c => c.WorkflowExecutionId).IsUnique();
    }
}
