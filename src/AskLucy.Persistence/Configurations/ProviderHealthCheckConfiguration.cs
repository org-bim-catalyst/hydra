using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="ProviderHealthCheck"/> — see UserChatConfiguration for the conventions this mirrors. No soft-delete query filter: this is an append-only operational log, not user-facing data (data-model.md).</summary>
public sealed class ProviderHealthCheckConfiguration : IEntityTypeConfiguration<ProviderHealthCheck>
{
    public void Configure(EntityTypeBuilder<ProviderHealthCheck> builder)
    {
        builder.ToTable("ProviderHealthChecks");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();

        builder.Property(h => h.CheckedAtUtc).IsRequired();
        builder.Property(h => h.IsHealthy).IsRequired();
        builder.Property(h => h.Detail).HasMaxLength(500);

        // specs/043 FR-016 - same string-conversion rule as AIProviderConfiguration.
        builder.Property(h => h.FailureKind).HasConversion<string>().HasMaxLength(40);
        builder.Property(h => h.FailureReason).HasMaxLength(500);

        builder.Property(h => h.CreatedBy).IsRequired();
        builder.Property(h => h.RowVersion).IsRowVersion();

        builder.HasIndex(h => new { h.ProviderId, h.CheckedAtUtc });

        builder.HasOne<AIProvider>()
            .WithMany()
            .HasForeignKey(h => h.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
