using AskLucy.Domain.KnowledgeBases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>Append-only log — no soft delete, no FK constraint on <c>KnowledgeBaseId</c> (an entry for a permanently purged knowledge base is deliberately retained, data-model.md), mirrors <c>ProviderHealthCheckConfiguration</c>.</summary>
public sealed class KnowledgeBaseAuditLogConfiguration : IEntityTypeConfiguration<KnowledgeBaseAuditLog>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseAuditLog> builder)
    {
        builder.ToTable("KnowledgeBaseAuditLogs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.OccurredAtUtc).IsRequired();
        builder.Property(a => a.DetailsJson).HasMaxLength(2000);

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => new { a.KnowledgeBaseId, a.OccurredAtUtc });
    }
}
