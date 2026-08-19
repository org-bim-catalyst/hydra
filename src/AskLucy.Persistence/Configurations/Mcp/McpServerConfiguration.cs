using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Mcp;

/// <summary>EF Core mapping for <see cref="McpServer"/> (data-model.md, spec.md FR-001-FR-010). <c>(Endpoint, Transport)</c> is unique platform-wide (clarification).</summary>
public sealed class McpServerConfiguration : IEntityTypeConfiguration<McpServer>
{
    public void Configure(EntityTypeBuilder<McpServer> builder)
    {
        builder.ToTable("McpServers");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Description).HasMaxLength(1000);
        // Bounded to keep the (Endpoint, Transport) unique index below SQL Server's 900-byte
        // nonclustered index key limit (well within any real MCP server URL's actual length).
        builder.Property(s => s.Endpoint).IsRequired().HasMaxLength(400);
        builder.Property(s => s.Transport).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.AuthenticationType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(s => s.RequiresUnauthenticatedConfirmation).IsRequired();
        builder.Property(s => s.AllowInsecureTransport).IsRequired();
        builder.Property(s => s.InsecureTransportJustification).HasMaxLength(1000);
        builder.Property(s => s.EndpointValidationOverride).IsRequired();
        builder.Property(s => s.EndpointValidationJustification).HasMaxLength(1000);
        builder.Property(s => s.IsEnabled).IsRequired();
        builder.Property(s => s.OwnerUserId).IsRequired();
        builder.Property(s => s.ConfigurationVersion).IsRequired();
        builder.Property(s => s.CapabilityRefreshIntervalMinutes).IsRequired();

        builder.Property(s => s.CreatedBy).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasQueryFilter(s => s.DeletedAtUtc == null);

        builder.HasIndex(s => new { s.Endpoint, s.Transport }).IsUnique();
        builder.HasIndex(s => s.OwnerUserId);
        builder.HasIndex(s => s.IsEnabled);
    }
}
