using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

public sealed class AgentExecutionEventConfiguration : IEntityTypeConfiguration<AgentExecutionEvent>
{
    public void Configure(EntityTypeBuilder<AgentExecutionEvent> builder)
    {
        builder.ToTable("AgentExecutionEvents");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AgentVersionId).IsRequired();
        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).IsRequired().HasMaxLength(50);
        builder.Property(e => e.SafeMetadataJson);
        builder.Property(e => e.OccurredAtUtc).IsRequired();

        builder.Property(e => e.RowVersion).IsRowVersion();

        // Cursor/reconciliation queries (contracts/agents-api.md GetAgentExecutionEventsQuery)
        // page by OccurredAtUtc within one execution.
        builder.HasIndex(e => new { e.AgentExecutionId, e.OccurredAtUtc });

        // The AgentExecution <-> AgentExecutionEvent relationship is configured from
        // AgentExecutionConfiguration.
    }
}
