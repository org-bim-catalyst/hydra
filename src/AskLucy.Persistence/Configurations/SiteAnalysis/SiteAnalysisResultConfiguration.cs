using AskLucy.Domain.SiteAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.SiteAnalysis;

/// <summary>EF Core mapping for <see cref="SiteAnalysisResult"/> — a child of <see cref="Domain.SiteAnalysis.SiteAnalysis"/>'s aggregate, no top-level <c>DbSet</c> (constitution &#167;5; see SiteAnalysisConfiguration for the owning navigation).</summary>
public sealed class SiteAnalysisResultConfiguration : IEntityTypeConfiguration<SiteAnalysisResult>
{
    public void Configure(EntityTypeBuilder<SiteAnalysisResult> builder)
    {
        builder.ToTable("SiteAnalysisResults");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.SiteAnalysisId).IsRequired();
        builder.Property(r => r.AnalysisType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.ContentJson);
        builder.Property(r => r.DataSource).HasMaxLength(100);
        builder.Property(r => r.ConfidenceLevel).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.DocumentId);
        builder.Property(r => r.FailureReason).HasMaxLength(4000);
        builder.Property(r => r.CompletedAtUtc).IsRequired();

        builder.Property(r => r.CreatedBy).IsRequired();
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasQueryFilter(r => r.DeletedAtUtc == null);

        builder.HasIndex(r => r.SiteAnalysisId);

        // data-model.md "Uniqueness" — one settled result per (analysis, specialist). A specialist
        // reports once; enforced here as a second, database-level guarantee alongside the
        // aggregate's own EnsureNotAlreadySettled check.
        builder.HasIndex(r => new { r.SiteAnalysisId, r.AnalysisType }).IsUnique();
    }
}
