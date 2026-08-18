using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

/// <summary>EF Core mapping for <see cref="AgentToolCall"/> — a child of <see cref="AgentExecutionStep"/> (data-model.md), queried directly via its own <c>DbSet</c> rather than through a navigation collection (mirrors <c>KnowledgeBaseTags</c>' own-<c>DbSet</c> convention for the same reason: history/tool-call queries filter across many steps at once, not one step at a time).</summary>
public sealed class AgentToolCallConfiguration : IEntityTypeConfiguration<AgentToolCall>
{
    public void Configure(EntityTypeBuilder<AgentToolCall> builder)
    {
        builder.ToTable("AgentToolCalls");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.ToolName).IsRequired().HasMaxLength(100);
        builder.Property(t => t.RiskLevel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.RequiredPermissionsJson).IsRequired();
        builder.Property(t => t.ValidatedInputJson).IsRequired();
        builder.Property(t => t.ValidatedOutputJson);
        builder.Property(t => t.FailureReason).HasMaxLength(2000);
        builder.Property(t => t.WasApprovalRequired).IsRequired();

        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasIndex(t => t.AgentExecutionStepId);
        builder.HasIndex(t => t.ToolName);

        builder.HasOne<AgentExecutionStep>()
            .WithMany()
            .HasForeignKey(t => t.AgentExecutionStepId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
