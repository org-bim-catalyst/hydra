using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="AIProvider"/> — see UserChatConfiguration for the conventions this mirrors.</summary>
public sealed class AIProviderConfiguration : IEntityTypeConfiguration<AIProvider>
{
    public void Configure(EntityTypeBuilder<AIProvider> builder)
    {
        builder.ToTable("AIProviders");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.ProviderKey).IsRequired().HasMaxLength(50);
        builder.Property(p => p.DisplayName).IsRequired().HasMaxLength(100);
        builder.Property(p => p.IsEnabled).IsRequired().HasDefaultValue(false);

        // Never selected/returned by any read projection except the one building the
        // outbound HTTP call — enforced at the Application layer, not by EF configuration
        // (data-model.md).
        builder.Property(p => p.CredentialCiphertext);
        builder.Property(p => p.CredentialLastRotatedAtUtc);

        builder.Property(p => p.HealthStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.HealthStatusCheckedAtUtc);

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.ProviderKey).IsUnique();

        // DefaultModelId points at an AIModel row that itself FKs back to this table via
        // ProviderId — Restrict avoids SQL Server's "multiple cascade paths" error between
        // the two tables; an admin clearing a provider's default model is an explicit action,
        // not something that should cascade.
        builder.HasOne<AIModel>()
            .WithMany()
            .HasForeignKey(p => p.DefaultModelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
