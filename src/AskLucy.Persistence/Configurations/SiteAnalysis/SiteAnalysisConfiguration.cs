using AskLucy.Domain.SiteAnalysis;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.SiteAnalysis;

/// <summary>EF Core mapping for <see cref="Domain.SiteAnalysis.SiteAnalysis"/> — persistence mapping lives entirely here, never as attributes on the Domain entity (constitution &#167;3).</summary>
public sealed class SiteAnalysisConfiguration : IEntityTypeConfiguration<Domain.SiteAnalysis.SiteAnalysis>
{
    public void Configure(EntityTypeBuilder<Domain.SiteAnalysis.SiteAnalysis> builder)
    {
        builder.ToTable("SiteAnalyses");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.UserChatId).IsRequired();
        builder.Property(a => a.SiteName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Latitude).IsRequired();
        builder.Property(a => a.Longitude).IsRequired();
        builder.Property(a => a.BoundaryGeoJson);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.WorkflowExecutionId);
        builder.Property(a => a.ExpectedResultCount).IsRequired();
        builder.Property(a => a.ClosingOutcomeReportedAtUtc);
        builder.Property(a => a.StartedAtUtc).IsRequired();
        builder.Property(a => a.CompletedAtUtc);

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasQueryFilter(a => a.DeletedAtUtc == null);

        // data-model.md "Persistence" — rehydrating a conversation's analyses on open (FR-017).
        builder.HasIndex(a => new { a.UserId, a.UserChatId, a.StartedAtUtc });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Results are a child of this aggregate — reachable only via this navigation (backed by a
        // private field), mirrors Agent.Tools/Agent.KnowledgeBases.
        builder.HasMany(a => a.Results)
            .WithOne()
            .HasForeignKey(r => r.SiteAnalysisId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Results).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
