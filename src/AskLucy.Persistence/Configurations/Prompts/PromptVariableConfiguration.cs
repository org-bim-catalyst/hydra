using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Prompts;

/// <summary>EF Core mapping for <see cref="PromptVariable"/> — immutable, cascades from its owning <see cref="PromptVersion"/>.</summary>
public sealed class PromptVariableConfiguration : IEntityTypeConfiguration<PromptVariable>
{
    public void Configure(EntityTypeBuilder<PromptVariable> builder)
    {
        builder.ToTable("PromptVariables");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.Name).IsRequired().HasMaxLength(100);
        builder.Property(v => v.Description).HasMaxLength(500);
        builder.Property(v => v.VariableType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(v => v.IsRequired).IsRequired();
        builder.Property(v => v.DefaultValue);
        builder.Property(v => v.ExampleValue);
        builder.Property(v => v.ValidationRulesJson).HasMaxLength(1000);
        builder.Property(v => v.OrderIndex).IsRequired();

        builder.Property(v => v.CreatedBy).IsRequired();
        builder.Property(v => v.RowVersion).IsRowVersion();

        builder.HasIndex(v => new { v.PromptVersionId, v.Name }).IsUnique();
    }
}
