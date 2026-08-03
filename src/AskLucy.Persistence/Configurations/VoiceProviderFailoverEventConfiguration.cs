using AskLucy.Domain.Ai;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="VoiceProviderFailoverEvent"/> — see ProviderHealthCheckConfiguration for the conventions this mirrors. No soft-delete query filter: this is an append-only operational log, not user-facing data (data-model.md).</summary>
public sealed class VoiceProviderFailoverEventConfiguration : IEntityTypeConfiguration<VoiceProviderFailoverEvent>
{
    public void Configure(EntityTypeBuilder<VoiceProviderFailoverEvent> builder)
    {
        builder.ToTable("VoiceProviderFailoverEvents");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.UserId).IsRequired();
        builder.Property(e => e.OccurredAtUtc).IsRequired();
        builder.Property(e => e.Direction).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Reason).HasMaxLength(500);

        builder.Property(e => e.CreatedBy).IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => new { e.UserId, e.OccurredAtUtc });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
