using AskLucy.Domain.Consent;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="CookieConsentRecord"/> (specs/004-cookie-consent-privacy,
/// data-model.md). Persistence mapping lives entirely here, never as attributes on the
/// Domain entity (constitution &#167;3).
/// </summary>
public sealed class CookieConsentRecordConfiguration : IEntityTypeConfiguration<CookieConsentRecord>
{
    public void Configure(EntityTypeBuilder<CookieConsentRecord> builder)
    {
        builder.ToTable("CookieConsentRecords");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.UserId).IsRequired();
        builder.Property(c => c.PolicyVersion).IsRequired().HasMaxLength(50);

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        // Every query in this feature is either "latest row for this user" or "all rows for
        // this user ordered by time" — both covered by this one composite index
        // (constitution §5, data-model.md Index).
        builder.HasIndex(c => new { c.UserId, c.CreatedAtUtc });

        // Cascade-delete with the owning account (spec.md Edge Cases: "retained or deleted
        // according to the same data-retention rules applied to the rest of their account
        // data"), mirroring UserChatConfiguration exactly.
        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.CookieConsentRecords)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
