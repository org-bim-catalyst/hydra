using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

public sealed class AgentExecutionCostConfiguration : IEntityTypeConfiguration<AgentExecutionCost>
{
    public void Configure(EntityTypeBuilder<AgentExecutionCost> builder)
    {
        builder.ToTable("AgentExecutionCosts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.EstimatedCost).IsRequired().HasColumnType("decimal(10,4)");
        builder.Property(c => c.Currency).IsRequired().HasMaxLength(3);

        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasIndex(c => c.AgentExecutionId).IsUnique();

        // The AgentExecution <-> AgentExecutionCost 1:1 relationship is configured from
        // AgentExecutionConfiguration.
    }
}
