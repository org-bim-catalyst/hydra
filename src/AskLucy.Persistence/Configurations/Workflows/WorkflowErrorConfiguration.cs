using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowError"/> — child of <see cref="WorkflowExecution"/>.</summary>
public sealed class WorkflowErrorConfiguration : IEntityTypeConfiguration<WorkflowError>
{
    public void Configure(EntityTypeBuilder<WorkflowError> builder)
    {
        builder.ToTable("WorkflowErrors");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Category).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.Message).IsRequired().HasMaxLength(2000);
        builder.Property(e => e.RetryCount).IsRequired();
        builder.Property(e => e.OccurredAtUtc).IsRequired();

        builder.Property(e => e.CreatedBy);
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => e.WorkflowExecutionId);
        builder.HasIndex(e => e.Category);
    }
}
