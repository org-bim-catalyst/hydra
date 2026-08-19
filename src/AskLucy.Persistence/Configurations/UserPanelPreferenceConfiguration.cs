using AskLucy.Domain.Panels;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="UserPanelPreference"/> — see UserVoicePreferenceConfiguration for the conventions this mirrors.</summary>
public sealed class UserPanelPreferenceConfiguration : IEntityTypeConfiguration<UserPanelPreference>
{
    public void Configure(EntityTypeBuilder<UserPanelPreference> builder)
    {
        builder.ToTable("UserPanelPreferences");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.OpacityPercent).IsRequired();

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.UserId).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
