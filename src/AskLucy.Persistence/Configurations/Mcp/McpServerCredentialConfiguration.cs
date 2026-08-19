using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Mcp;

/// <summary>EF Core mapping for <see cref="McpServerCredential"/> (spec.md FR-045-FR-047, research.md Decision 7). <see cref="McpServerCredential.CiphertextBlob"/> is already encrypted (Data Protection) by the time it reaches this layer.</summary>
public sealed class McpServerCredentialConfiguration : IEntityTypeConfiguration<McpServerCredential>
{
    public void Configure(EntityTypeBuilder<McpServerCredential> builder)
    {
        builder.ToTable("McpServerCredentials");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.CiphertextBlob).IsRequired();
        builder.Property(c => c.RotatedAtUtc).IsRequired();
        builder.Property(c => c.RotatedByUserId).IsRequired();

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasQueryFilter(c => c.DeletedAtUtc == null);

        builder.HasIndex(c => c.McpServerId).IsUnique();

        builder.HasOne<McpServer>()
            .WithMany()
            .HasForeignKey(c => c.McpServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
