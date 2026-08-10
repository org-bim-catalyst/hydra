using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

public sealed class AgentToolConfiguration : IEntityTypeConfiguration<AgentTool>
{
    public void Configure(EntityTypeBuilder<AgentTool> builder)
    {
        builder.ToTable("AgentTools");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.ToolName).IsRequired().HasMaxLength(100);
        builder.Property(t => t.ConfigurationJson);

        builder.Property(t => t.CreatedBy).IsRequired();
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasIndex(t => new { t.AgentId, t.ToolName }).IsUnique();

        // The Agent <-> AgentTool relationship is configured from AgentConfiguration.
    }
}
