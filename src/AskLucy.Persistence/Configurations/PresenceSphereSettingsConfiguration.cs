using AskLucy.Domain.Appearance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>specs/080 — EF Core mapping for the single <see cref="PresenceSphereSettings"/> row.</summary>
public sealed class PresenceSphereSettingsConfiguration : IEntityTypeConfiguration<PresenceSphereSettings>
{
    public void Configure(EntityTypeBuilder<PresenceSphereSettings> builder)
    {
        builder.ToTable("PresenceSphereSettings");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.DotSizeMultiplier).HasPrecision(4, 2).IsRequired();
        builder.Property(s => s.CardFillPercent).IsRequired();
        builder.Property(s => s.ZoomEnabled).IsRequired();

        builder.Property(s => s.CreatedBy).IsRequired().HasMaxLength(450);
        builder.Property(s => s.ModifiedBy).HasMaxLength(450);
        builder.Property(s => s.RowVersion).IsRowVersion();
    }
}
