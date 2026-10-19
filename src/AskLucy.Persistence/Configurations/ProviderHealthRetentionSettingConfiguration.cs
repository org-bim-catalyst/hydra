using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>
/// specs/082 — EF Core mapping for the single <see cref="ProviderHealthRetentionSetting"/> row. The seed is the effective
/// configuration value at release (30 days); a stored value wins from then on.
/// </summary>
public sealed class ProviderHealthRetentionSettingConfiguration : IEntityTypeConfiguration<ProviderHealthRetentionSetting>
{
    public void Configure(EntityTypeBuilder<ProviderHealthRetentionSetting> builder)
    {
        builder.ToTable("ProviderHealthRetentionSettings");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.RetentionDays).IsRequired();

        builder.Property(s => s.CreatedBy).IsRequired().HasMaxLength(450);
        builder.Property(s => s.ModifiedBy).HasMaxLength(450);
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasData(new
        {
            Id = ProviderHealthRetentionSetting.SingletonId,
            RetentionDays = ProviderHealthRetentionSetting.DefaultRetentionDays,
            CreatedAtUtc = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc),
            CreatedBy = "system",
        });
    }
}
