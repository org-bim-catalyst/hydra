using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentStatisticsConfiguration : IEntityTypeConfiguration<DocumentStatistics>
{
    public void Configure(EntityTypeBuilder<DocumentStatistics> builder)
    {
        builder.ToTable("DocumentStatistics");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Scope).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.TotalDocuments).IsRequired();
        builder.Property(s => s.TotalStorageBytes).IsRequired();
        builder.Property(s => s.FileTypeDistributionJson).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(s => s.LanguageDistributionJson).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(s => s.ComputedAtUtc).IsRequired();

        builder.Property(s => s.CreatedBy).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => new { s.Scope, s.OwnerId }).IsUnique();
    }
}
