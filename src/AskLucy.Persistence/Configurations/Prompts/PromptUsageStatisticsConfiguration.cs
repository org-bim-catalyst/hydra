using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Prompts;

/// <summary>EF Core mapping for <see cref="PromptUsageStatistics"/> — 1:1 with <see cref="Prompt"/>, successful-execution-only counters (spec.md FR-051, Clarifications 2026-08-10).</summary>
public sealed class PromptUsageStatisticsConfiguration : IEntityTypeConfiguration<PromptUsageStatistics>
{
    public void Configure(EntityTypeBuilder<PromptUsageStatistics> builder)
    {
        builder.ToTable("PromptUsageStatistics");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.SuccessfulExecutionCount).IsRequired().HasDefaultValue(0);

        builder.Property(s => s.CreatedBy).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => s.PromptId).IsUnique();
        builder.HasIndex(s => s.LastSuccessfulUseAtUtc);

        builder.HasOne<Prompt>()
            .WithMany()
            .HasForeignKey(s => s.PromptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
