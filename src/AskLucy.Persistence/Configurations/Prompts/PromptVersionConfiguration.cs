using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Prompts;

/// <summary>EF Core mapping for <see cref="PromptVersion"/> — append-only, cascades from its owning <see cref="Prompt"/> (no meaning independent of it).</summary>
public sealed class PromptVersionConfiguration : IEntityTypeConfiguration<PromptVersion>
{
    public void Configure(EntityTypeBuilder<PromptVersion> builder)
    {
        builder.ToTable("PromptVersions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.VersionNumber).IsRequired();
        builder.Property(v => v.SystemInstructions);
        builder.Property(v => v.DeveloperInstructions);
        builder.Property(v => v.UserInstructions).IsRequired();
        builder.Property(v => v.ContextText);
        builder.Property(v => v.ExamplesText);
        builder.Property(v => v.OutputInstructions);
        builder.Property(v => v.Constraints);
        builder.Property(v => v.ProviderKey).HasMaxLength(100);
        builder.Property(v => v.ModelKey).HasMaxLength(100);
        builder.Property(v => v.Temperature).HasColumnType("decimal(3,2)");
        builder.Property(v => v.ChangeDescription).HasMaxLength(500);

        builder.Property(v => v.CreatedBy).IsRequired();
        builder.Property(v => v.RowVersion).IsRowVersion();

        builder.HasIndex(v => new { v.PromptId, v.VersionNumber }).IsUnique();

        // The Prompt <-> PromptVersion relationship itself is configured from PromptConfiguration
        // (HasMany(p => p.Versions)), not here — mirrors KnowledgeBase.Tags' single-side
        // configuration convention.
        builder.HasMany(v => v.Variables)
            .WithOne()
            .HasForeignKey(pv => pv.PromptVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Variables).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
