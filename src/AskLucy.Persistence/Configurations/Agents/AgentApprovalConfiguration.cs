using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

public sealed class AgentApprovalConfiguration : IEntityTypeConfiguration<AgentApproval>
{
    public void Configure(EntityTypeBuilder<AgentApproval> builder)
    {
        builder.ToTable("AgentApprovals");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.IntendedActionDescription).IsRequired().HasMaxLength(2000);
        builder.Property(a => a.IntendedParametersJson).IsRequired();
        builder.Property(a => a.Decision).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.WasPolicyBased).IsRequired();

        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => a.AgentExecutionId);

        // Restrict, not Cascade — AgentExecution already cascades directly into AgentApproval
        // (AgentExecutionId FK, configured in AgentExecutionConfiguration) AND transitively via
        // AgentExecution -> AgentExecutionStep -> AgentToolCall, so a second cascade edge here
        // would give SQL Server two convergent cascade paths into this table from the same
        // ancestor ("may cause cycles or multiple cascade paths" — same conflict already fixed on
        // AgentKnowledgeBaseConfiguration). The AgentExecutionId cascade already deletes this row
        // when its execution goes; this FK exists purely for referential integrity on the
        // optional tool-call link.
        builder.HasOne<AgentToolCall>()
            .WithMany()
            .HasForeignKey(a => a.AgentToolCallId)
            .OnDelete(DeleteBehavior.Restrict);

        // AgentPolicy is a soft reference (MatchedAgentPolicyId, no FK) — a policy can be
        // deleted after having auto-approved past actions; the approval record must still be
        // retained for the audit trail (FR-028), mirroring KnowledgeBaseAuditLogs' rationale.
        // The AgentExecution <-> AgentApproval relationship is configured from
        // AgentExecutionConfiguration.
    }
}
