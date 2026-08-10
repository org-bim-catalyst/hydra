using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

/// <summary>EF Core mapping for <see cref="AgentPolicy"/> (research.md Decision 1 — <see cref="AgentPolicy.OrganizationId"/> is reserved and unused this release, indexed anyway so a future multi-tenancy migration can filter on it without a schema change).</summary>
public sealed class AgentPolicyConfiguration : IEntityTypeConfiguration<AgentPolicy>
{
    public void Configure(EntityTypeBuilder<AgentPolicy> builder)
    {
        builder.ToTable("AgentPolicies");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.ToolName).IsRequired().HasMaxLength(100);
        builder.Property(p => p.ConditionsJson);
        builder.Property(p => p.CreatedByUserId).IsRequired();
        builder.Property(p => p.IsEnabled).IsRequired().HasDefaultValue(true);

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasQueryFilter(p => p.DeletedAtUtc == null);

        builder.HasIndex(p => p.OrganizationId);

        // Composite, not two single-column indexes — AgentPolicyEvaluator's
        // ListEnabledByToolNameAsync (checked on every High/Critical-risk tool call) always
        // filters on both columns together, never either alone.
        builder.HasIndex(p => new { p.ToolName, p.IsEnabled });
    }
}
