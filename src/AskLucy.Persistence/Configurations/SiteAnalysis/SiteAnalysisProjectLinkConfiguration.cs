using AskLucy.Domain.SiteAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.SiteAnalysis;

/// <summary>EF Core mapping for <see cref="SiteAnalysisProjectLink"/> (data-model.md). Exactly one link per <c>UserChatId</c> (FR-001d).</summary>
public sealed class SiteAnalysisProjectLinkConfiguration : IEntityTypeConfiguration<SiteAnalysisProjectLink>
{
    public void Configure(EntityTypeBuilder<SiteAnalysisProjectLink> builder)
    {
        builder.ToTable("SiteAnalysisProjectLinks");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.UserChatId).IsRequired();
        builder.Property(l => l.TheDigitalCoreProjectId).IsRequired().HasMaxLength(200);
        builder.Property(l => l.LinkSource).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(l => l.SiteName).IsRequired().HasMaxLength(300);
        builder.Property(l => l.ResolvedLatitude).HasPrecision(9, 6);
        builder.Property(l => l.ResolvedLongitude).HasPrecision(9, 6);

        builder.Property(l => l.CreatedBy).IsRequired();
        builder.Property(l => l.RowVersion).IsRowVersion();

        builder.HasQueryFilter(l => l.DeletedAtUtc == null);

        builder.HasIndex(l => l.UserChatId).IsUnique();
        builder.HasIndex(l => l.TheDigitalCoreProjectId);
    }
}
