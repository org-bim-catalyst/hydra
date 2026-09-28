using AskLucy.Domain.Ai.Dictation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>specs/078 — EF Core mapping for the single <see cref="DictationEngineSetting"/> row.</summary>
public sealed class DictationEngineSettingConfiguration : IEntityTypeConfiguration<DictationEngineSetting>
{
    private const int EnumLength = 32;

    public void Configure(EntityTypeBuilder<DictationEngineSetting> builder)
    {
        builder.ToTable("DictationEngineSettings");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.PrimaryEngine).HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(s => s.PushToTalkEngine).HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(s => s.LocalWhisperModelId);
        builder.Property(s => s.State).HasConversion<string>().HasMaxLength(EnumLength).IsRequired();
        builder.Property(s => s.SuspendedAtUtc);
        builder.Property(s => s.SuspensionReason).HasMaxLength(DictationEngineSetting.MaxSuspensionReasonLength);
        builder.Property(s => s.SuspendedEngine).HasConversion<string>().HasMaxLength(EnumLength);
        builder.Property(s => s.LastRevertedAtUtc);
        builder.Property(s => s.LastRevertReason).HasConversion<string>().HasMaxLength(EnumLength);
        builder.Property(s => s.LastRevertedFrom).HasConversion<string>().HasMaxLength(EnumLength);

        builder.Property(s => s.CreatedBy).IsRequired().HasMaxLength(450);
        builder.Property(s => s.ModifiedBy).HasMaxLength(450);
        builder.Property(s => s.RowVersion).IsRowVersion();
    }
}
