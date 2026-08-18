using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Prompts;

/// <summary>EF Core mapping for <see cref="PromptExecutionResult"/> — 1:1 with <see cref="PromptExecution"/>, only created for `Origin: TestingWorkspace` (spec.md FR-042).</summary>
public sealed class PromptExecutionResultConfiguration : IEntityTypeConfiguration<PromptExecutionResult>
{
    public void Configure(EntityTypeBuilder<PromptExecutionResult> builder)
    {
        builder.ToTable("PromptExecutionResults");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.OutputText).IsRequired();
        builder.Property(r => r.EstimatedCostUsd).HasColumnType("decimal(18,6)");

        builder.Property(r => r.CreatedBy).IsRequired();
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasIndex(r => r.PromptExecutionId).IsUnique();

        builder.HasOne<PromptExecution>()
            .WithMany()
            .HasForeignKey(r => r.PromptExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
