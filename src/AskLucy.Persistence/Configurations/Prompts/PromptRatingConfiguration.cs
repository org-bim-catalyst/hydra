using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Prompts;

/// <summary>EF Core mapping for <see cref="PromptRating"/> — 1:1 with <see cref="PromptExecution"/> (spec.md FR-044).</summary>
public sealed class PromptRatingConfiguration : IEntityTypeConfiguration<PromptRating>
{
    public void Configure(EntityTypeBuilder<PromptRating> builder)
    {
        builder.ToTable("PromptRatings");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.RatingValue).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.RatedByActor).IsRequired();

        builder.Property(r => r.CreatedBy).IsRequired();
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasIndex(r => r.PromptExecutionId).IsUnique();

        builder.HasOne<PromptExecution>()
            .WithMany()
            .HasForeignKey(r => r.PromptExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
