using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Mcp;

/// <summary>EF Core mapping for <see cref="McpCapabilitySnapshot"/> (spec.md FR-011, FR-015-FR-018) — append-only, restricted from cascading with its server (data-model.md Delete behavior).</summary>
public sealed class McpCapabilitySnapshotConfiguration : IEntityTypeConfiguration<McpCapabilitySnapshot>
{
    public void Configure(EntityTypeBuilder<McpCapabilitySnapshot> builder)
    {
        builder.ToTable("McpCapabilitySnapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.DiscoveredAtUtc).IsRequired();
        builder.Property(s => s.SnapshotVersion).IsRequired();
        builder.Property(s => s.DeclaredCapabilitiesJson).IsRequired();
        builder.Property(s => s.ChangeSummaryJson);
        builder.Property(s => s.WasSuccessful).IsRequired();
        builder.Property(s => s.FailureCategory).HasConversion<string>().HasMaxLength(30);

        builder.Property(s => s.CreatedBy).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasQueryFilter(s => s.DeletedAtUtc == null);

        builder.HasIndex(s => new { s.McpServerId, s.SnapshotVersion }).IsUnique();

        builder.HasOne<McpServer>()
            .WithMany()
            .HasForeignKey(s => s.McpServerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
