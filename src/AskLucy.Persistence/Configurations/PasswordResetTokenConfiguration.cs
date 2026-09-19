using AskLucy.Domain.Authentication;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("PasswordResetTokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.UserId).IsRequired().HasMaxLength(450);
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(t => t.EmailAtIssue).IsRequired().HasMaxLength(256);
        builder.Property(t => t.RequestedFromIp).HasMaxLength(45);

        // Redemption looks the token up by hash alone, so this index carries the hot path as well
        // as enforcing that a hash collision can never yield two rows.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Serves both the per-email issue throttle (FR-004) and bulk supersession (FR-006).
        builder.HasIndex(t => new { t.UserId, t.CreatedAtUtc });

        // Cascade rather than the no-FK approach RefreshToken takes: a deleted account must not
        // leave behind rows that could still name it, and there is no audit value in a reset
        // token whose account no longer exists.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
