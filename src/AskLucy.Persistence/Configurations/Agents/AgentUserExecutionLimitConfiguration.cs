using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

public sealed class AgentUserExecutionLimitConfiguration : IEntityTypeConfiguration<AgentUserExecutionLimit>
{
    public void Configure(EntityTypeBuilder<AgentUserExecutionLimit> builder)
    {
        builder.ToTable("AgentUserExecutionLimits");

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
