using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Mcp;

/// <summary>EF Core mapping for <see cref="McpAuditLog"/> (spec.md FR-058-FR-060) — deliberately not hard-FK'd to <c>McpServers</c> (data-model.md), mirrors <c>AgentAuditLogConfiguration</c>.</summary>
public sealed class McpAuditLogConfiguration : IEntityTypeConfiguration<McpAuditLog>
{
    public void Configure(EntityTypeBuilder<McpAuditLog> builder)
    {
        builder.ToTable("McpAuditLogs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.FailureCategory).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.DetailsJson).IsRequired();
        builder.Property(a => a.OccurredAtUtc).IsRequired();

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => a.McpServerId);
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.Action);
    }
}
