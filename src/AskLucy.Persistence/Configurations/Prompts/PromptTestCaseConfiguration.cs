using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Prompts;

/// <summary>EF Core mapping for <see cref="PromptTestCase"/> (spec.md FR-043).</summary>
public sealed class PromptTestCaseConfiguration : IEntityTypeConfiguration<PromptTestCase>
{
    public void Configure(EntityTypeBuilder<PromptTestCase> builder)
    {
        builder.ToTable("PromptTestCases");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.VariableValuesJson).IsRequired();
        builder.Property(t => t.ExpectedOutput);
        builder.Property(t => t.EvaluationCriteria).HasMaxLength(1000);
        builder.Property(t => t.ProviderKey).IsRequired().HasMaxLength(100);
        builder.Property(t => t.ModelKey).IsRequired().HasMaxLength(100);

        builder.Property(t => t.CreatedBy).IsRequired();
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasQueryFilter(t => t.DeletedAtUtc == null);

        builder.HasIndex(t => t.PromptId);

        builder.HasOne<Prompt>()
            .WithMany()
            .HasForeignKey(t => t.PromptId)
            .OnDelete(DeleteBehavior.Cascade);

        // No FK to PromptExecution — a test case must outlive the specific execution it was
        // captured from (data-model.md).
        builder.HasIndex(t => t.SourceExecutionId);
    }
}
