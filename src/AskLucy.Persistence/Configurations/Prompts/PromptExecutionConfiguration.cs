using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Prompts;

/// <summary>EF Core mapping for <see cref="PromptExecution"/> — immutable after creation (spec.md FR-040-FR-046, FR-080).</summary>
public sealed class PromptExecutionConfiguration : IEntityTypeConfiguration<PromptExecution>
{
    public void Configure(EntityTypeBuilder<PromptExecution> builder)
    {
        builder.ToTable("PromptExecutions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Origin).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.ProviderKey).IsRequired().HasMaxLength(100);
        builder.Property(e => e.ModelKey).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Temperature).HasColumnType("decimal(3,2)");
        builder.Property(e => e.ResolvedVariableValuesJson).IsRequired();
        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.ErrorDetail).HasMaxLength(1000);

        builder.Property(e => e.CreatedBy).IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => new { e.PromptId, e.CreatedAtUtc });
        builder.HasIndex(e => e.PromptVersionId);
        builder.HasIndex(e => e.ResultMessageId);

        builder.HasOne<Prompt>()
            .WithMany()
            .HasForeignKey(e => e.PromptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PromptVersion>()
            .WithMany()
            .HasForeignKey(e => e.PromptVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
