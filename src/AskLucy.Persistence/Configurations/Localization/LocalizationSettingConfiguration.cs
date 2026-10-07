using AskLucy.Domain.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Localization;

/// <summary>EF Core mapping for the single <see cref="LocalizationSetting"/> row (specs/067 FR-044a); the migration seeds it disabled, English only.</summary>
public sealed class LocalizationSettingConfiguration : IEntityTypeConfiguration<LocalizationSetting>
{
    public void Configure(EntityTypeBuilder<LocalizationSetting> builder)
    {
        builder.ToTable("LocalizationSettings");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.IsEnabled).IsRequired();
        builder.Property(s => s.SupportedLanguagesJson).HasMaxLength(LocalizationSetting.SupportedLanguagesMaxLength).IsRequired();

        builder.Property(s => s.CreatedBy).IsRequired().HasMaxLength(450);
        builder.Property(s => s.ModifiedBy).HasMaxLength(450);
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasData(new
        {
            Id = LocalizationSetting.SingletonId,
            IsEnabled = false,
            SupportedLanguagesJson = "[\"en\"]",
            CreatedAtUtc = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc),
            CreatedBy = "system",
        });
    }
}
