using AskLucy.Domain.Memory;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Memory;

public sealed class MemoryCategoryPreferenceConfiguration : IEntityTypeConfiguration<MemoryCategoryPreference>
{
    public void Configure(EntityTypeBuilder<MemoryCategoryPreference> builder)
    {
        builder.ToTable("MemoryCategoryPreferences");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.Category).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.ApprovalMode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.IsEnabled).IsRequired().HasDefaultValue(true);

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => new { p.UserId, p.Category }).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
