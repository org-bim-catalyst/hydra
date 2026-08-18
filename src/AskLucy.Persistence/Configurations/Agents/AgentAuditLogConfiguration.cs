using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

/// <summary>EF Core mapping for <see cref="AgentAuditLog"/> — deliberately not hard-FK'd to <c>AgentExecutions</c> (data-model.md), mirrors <c>KnowledgeBaseAuditLogConfiguration</c>.</summary>
public sealed class AgentAuditLogConfiguration : IEntityTypeConfiguration<AgentAuditLog>
{
    public void Configure(EntityTypeBuilder<AgentAuditLog> builder)
    {
        builder.ToTable("AgentAuditLogs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.DetailsJson).IsRequired();
        builder.Property(a => a.OccurredAtUtc).IsRequired();

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => a.AgentExecutionId);
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.Action);
    }
}
