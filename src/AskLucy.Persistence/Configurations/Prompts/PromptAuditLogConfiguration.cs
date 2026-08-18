using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Prompts;

/// <summary>EF Core mapping for <see cref="PromptAuditLog"/> — append-only, no cascade FK so it survives a hard-purged prompt (spec.md FR-090, mirrors <c>KnowledgeBaseAuditLog</c>/<c>MemoryAuditLog</c>).</summary>
public sealed class PromptAuditLogConfiguration : IEntityTypeConfiguration<PromptAuditLog>
{
    public void Configure(EntityTypeBuilder<PromptAuditLog> builder)
    {
        builder.ToTable("PromptAuditLogs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.ActorId).IsRequired();
        builder.Property(a => a.DetailsJson).HasMaxLength(2000);

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        // No FK to Prompt — deliberately, per the doc comment: must survive a hard-purged prompt.
        builder.HasIndex(a => a.PromptId);
    }
}
