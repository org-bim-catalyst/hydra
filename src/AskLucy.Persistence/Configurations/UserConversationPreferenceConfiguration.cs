using AskLucy.Domain.Chats;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="UserConversationPreference"/> (specs/045 FR-032) — mirrors
/// <see cref="UserPanelPreferenceConfiguration"/> exactly, including the unique index on
/// <c>UserId</c> and the cascade from the owning identity user.
/// </summary>
public sealed class UserConversationPreferenceConfiguration : IEntityTypeConfiguration<UserConversationPreference>
{
    public void Configure(EntityTypeBuilder<UserConversationPreference> builder)
    {
        builder.ToTable("UserConversationPreferences");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.UserId).IsRequired();

        // Defaulted in the database as well as in the domain: an absent row already means
        // "enabled", so a row inserted by any other path must mean the same thing.
        builder.Property(p => p.SuggestedActionsEnabled).IsRequired().HasDefaultValue(true);

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.UserId).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
