using AskLucy.Domain.Memory;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Memory;

public sealed class MemoryPreferenceConfiguration : IEntityTypeConfiguration<MemoryPreference>
{
    public void Configure(EntityTypeBuilder<MemoryPreference> builder)
    {
        builder.ToTable("MemoryPreferences");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.MemoryEnabled).IsRequired().HasDefaultValue(true);

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.UserId).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
