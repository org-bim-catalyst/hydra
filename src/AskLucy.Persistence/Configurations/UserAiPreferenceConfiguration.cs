using AskLucy.Domain.Ai;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="UserAiPreference"/> — see UserChatConfiguration for the conventions this mirrors.</summary>
public sealed class UserAiPreferenceConfiguration : IEntityTypeConfiguration<UserAiPreference>
{
    public void Configure(EntityTypeBuilder<UserAiPreference> builder)
    {
        builder.ToTable("UserAiPreferences");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.DefaultGenerationParametersJson);

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.UserId).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AIProvider>()
            .WithMany()
            .HasForeignKey(p => p.DefaultProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AIModel>()
            .WithMany()
            .HasForeignKey(p => p.DefaultModelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
