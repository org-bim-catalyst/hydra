using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="AIModel"/> — see UserChatConfiguration for the conventions this mirrors.</summary>
public sealed class AIModelConfiguration : IEntityTypeConfiguration<AIModel>
{
    public void Configure(EntityTypeBuilder<AIModel> builder)
    {
        builder.ToTable("AIModels");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.ModelKey).IsRequired().HasMaxLength(100);
        builder.Property(m => m.DisplayName).IsRequired().HasMaxLength(150);
        builder.Property(m => m.ContextWindowTokens).IsRequired();
        builder.Property(m => m.MaxOutputTokens).IsRequired();

        builder.Property(m => m.SupportsStreaming).IsRequired();
        builder.Property(m => m.SupportsVision).IsRequired();
        builder.Property(m => m.SupportsFunctionCalling).IsRequired();
        builder.Property(m => m.SupportsJsonMode).IsRequired();
        builder.Property(m => m.SupportsReasoning).IsRequired();
        builder.Property(m => m.SupportsEmbeddings).IsRequired();
        builder.Property(m => m.SupportsImageInput).IsRequired();
        builder.Property(m => m.SupportsImageOutput).IsRequired();
        builder.Property(m => m.SupportsAudio).IsRequired();

        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.ReleaseDate);

        // Optional owned type: null on the entity means "pricing unknown" (FR-022) — EF maps
        // that as both columns being NULL, never a fabricated 0.
        builder.OwnsOne(m => m.Pricing, pricing =>
        {
            pricing.Property(p => p.InputPerMillionTokensUsd)
                .HasColumnName("InputPricePerMillionTokensUsd")
                .HasColumnType("decimal(18,6)");
            pricing.Property(p => p.OutputPerMillionTokensUsd)
                .HasColumnName("OutputPricePerMillionTokensUsd")
                .HasColumnType("decimal(18,6)");
        });

        builder.Property(m => m.CreatedBy).IsRequired();
        builder.Property(m => m.RowVersion).IsRowVersion();

        builder.HasIndex(m => new { m.ProviderId, m.ModelKey }).IsUnique();

        builder.HasOne<AIProvider>()
            .WithMany()
            .HasForeignKey(m => m.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
