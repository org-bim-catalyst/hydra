using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

public sealed class AgentMemoryPolicyConfiguration : IEntityTypeConfiguration<AgentMemoryPolicy>
{
    public void Configure(EntityTypeBuilder<AgentMemoryPolicy> builder)
    {
        builder.ToTable("AgentMemoryPolicies");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.AllowRead).IsRequired();
        builder.Property(m => m.AllowWriteProposals).IsRequired();
        builder.Property(m => m.PreApprovedCategoriesJson);

        builder.Property(m => m.CreatedBy).IsRequired();
        builder.Property(m => m.RowVersion).IsRowVersion();

        builder.HasIndex(m => m.AgentId).IsUnique();

        // The Agent <-> AgentMemoryPolicy 1:1 relationship is configured from AgentConfiguration.
    }
}
