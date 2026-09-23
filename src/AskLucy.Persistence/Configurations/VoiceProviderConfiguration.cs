using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="VoiceProvider"/> (specs/070) — mirrors <see cref="AIProviderConfiguration"/>'s credential conventions.</summary>
public sealed class VoiceProviderConfiguration : IEntityTypeConfiguration<VoiceProvider>
{
    public void Configure(EntityTypeBuilder<VoiceProvider> builder)
    {
        builder.ToTable("VoiceProviders");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.ProviderKey).IsRequired().HasMaxLength(50);
        builder.Property(p => p.DisplayName).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Priority).IsRequired();
        builder.Property(p => p.DefaultVoiceId).HasMaxLength(VoiceProvider.MaxVoiceIdLength);

        // Never projected into a read DTO — see AIProviderConfiguration.
        builder.Property(p => p.CredentialCiphertext);
        builder.Property(p => p.CredentialHint).HasMaxLength(20);
        builder.Property(p => p.CredentialLastRotatedAtUtc);

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.ProviderKey).IsUnique();
        builder.HasIndex(p => p.Priority);
    }
}
