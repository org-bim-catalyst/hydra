using AskLucy.Domain.Ai;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="UserVoicePreference"/> — see UserAiPreferenceConfiguration for the conventions this mirrors.</summary>
public sealed class UserVoicePreferenceConfiguration : IEntityTypeConfiguration<UserVoicePreference>
{
    public void Configure(EntityTypeBuilder<UserVoicePreference> builder)
    {
        builder.ToTable("UserVoicePreferences");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.ConversationMode).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.SelectedVoiceId).HasMaxLength(100);
        builder.Property(p => p.PreferredMicrophoneDeviceId).HasMaxLength(200);
        builder.Property(p => p.PreferredSpeakerDeviceId).HasMaxLength(200);
        builder.Property(p => p.DefaultLanguage).HasMaxLength(10);

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.UserId).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
