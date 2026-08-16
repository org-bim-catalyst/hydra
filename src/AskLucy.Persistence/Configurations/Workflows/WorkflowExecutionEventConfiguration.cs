using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowExecutionEvent"/> — append-only, child of <see cref="WorkflowExecution"/>.</summary>
public sealed class WorkflowExecutionEventConfiguration : IEntityTypeConfiguration<WorkflowExecutionEvent>
{
    public void Configure(EntityTypeBuilder<WorkflowExecutionEvent> builder)
    {
        builder.ToTable("WorkflowExecutionEvents");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).IsRequired().HasMaxLength(50);
        builder.Property(e => e.SafeMetadataJson);
        builder.Property(e => e.OccurredAtUtc).IsRequired();

        builder.Property(e => e.CreatedBy);
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => new { e.WorkflowExecutionId, e.OccurredAtUtc });
    }
}
