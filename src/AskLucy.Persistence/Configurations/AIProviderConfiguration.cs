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

        // A string, never an ordinal, matching HealthStatus below.
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Never selected/returned by any read projection except the one building the
        // outbound HTTP call — enforced at the Application layer, not by EF configuration
        // (data-model.md).
        builder.Property(p => p.CredentialCiphertext);

        // Unlike CredentialCiphertext above, safe to project into read DTOs — a vendor-style
        // fingerprint, never the raw credential (specs/066 data-model.md).
        builder.Property(p => p.CredentialHint).HasMaxLength(20);
        builder.Property(p => p.CredentialLastRotatedAtUtc);

        builder.Property(p => p.HealthStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.HealthStatusCheckedAtUtc);

        // specs/043 FR-016. Stored as a string, never an ordinal, matching the HealthStatus
        // and AIModelStatus convention above: an ordinal silently remaps if the enum is ever
        // reordered. Both are nullable - non-null only while HealthStatus is Unhealthy.
        builder.Property(p => p.HealthFailureKind).HasConversion<string>().HasMaxLength(40);
        builder.Property(p => p.HealthFailureReason).HasMaxLength(500);

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
