using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

public sealed class AgentExecutionErrorConfiguration : IEntityTypeConfiguration<AgentExecutionError>
{
    public void Configure(EntityTypeBuilder<AgentExecutionError> builder)
    {
        builder.ToTable("AgentExecutionErrors");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Category).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.Message).IsRequired().HasMaxLength(2000);
        builder.Property(e => e.RetryCount).IsRequired();
        builder.Property(e => e.OccurredAtUtc).IsRequired();

        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => e.AgentExecutionId);

        // AgentExecutionStepId is a soft reference — a step row and its error can be created in
        // the same unit of work; no FK constraint needed beyond the shared AgentExecutionId.
        // The AgentExecution <-> AgentExecutionError relationship is configured from
        // AgentExecutionConfiguration.
    }
}
