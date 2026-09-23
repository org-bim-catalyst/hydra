using AskLucy.Domain.CustomModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="CustomModelOverwrittenFile"/> (specs/072 FR-041).</summary>
public sealed class CustomModelOverwrittenFileConfiguration : IEntityTypeConfiguration<CustomModelOverwrittenFile>
{
    public void Configure(EntityTypeBuilder<CustomModelOverwrittenFile> builder)
    {
        builder.ToTable("CustomModelOverwrittenFiles");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).UseIdentityColumn();

        builder.Property(f => f.RelativePath).IsRequired().HasMaxLength(CustomModelOverwrittenFile.MaxRelativePathLength);
        builder.Property(f => f.PreviousSizeBytes).IsRequired();
        builder.Property(f => f.OverwrittenAtUtc).IsRequired();

        builder.HasIndex(f => f.CustomModelId);
    }
}
