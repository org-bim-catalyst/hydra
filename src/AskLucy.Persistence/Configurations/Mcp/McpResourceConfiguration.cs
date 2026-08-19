using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Mcp;

/// <summary>EF Core mapping for <see cref="McpResource"/> (spec.md FR-036-FR-040).</summary>
public sealed class McpResourceConfiguration : IEntityTypeConfiguration<McpResource>
{
    public void Configure(EntityTypeBuilder<McpResource> builder)
    {
        builder.ToTable("McpResources");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // Bounded to keep the unique nvarchar index below SQL Server's 900-byte key limit.
        builder.Property(r => r.NamespacedName).IsRequired().HasMaxLength(400);
        builder.Property(r => r.Uri).IsRequired().HasMaxLength(2000);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Description).HasMaxLength(2000);
        builder.Property(r => r.ContentType).HasMaxLength(200);
        builder.Property(r => r.IsAvailable).IsRequired();

        builder.Property(r => r.CreatedBy).IsRequired();
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasQueryFilter(r => r.DeletedAtUtc == null);

        builder.HasIndex(r => r.NamespacedName).IsUnique();
        builder.HasIndex(r => r.McpServerId);

        builder.HasOne<McpServer>()
            .WithMany()
            .HasForeignKey(r => r.McpServerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<McpCapabilitySnapshot>()
            .WithMany()
            .HasForeignKey(r => r.McpCapabilitySnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
