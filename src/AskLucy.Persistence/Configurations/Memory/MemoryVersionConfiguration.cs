using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Persistence.Configurations.Memory;

public sealed class MemoryVersionConfiguration : IEntityTypeConfiguration<MemoryVersion>
{
    public void Configure(EntityTypeBuilder<MemoryVersion> builder)
    {
        builder.ToTable("MemoryVersions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        // PreviousContent's encryption converter is applied in AskLucyDbContext.OnModelCreating.
        builder.Property(v => v.PreviousContent).IsRequired();
        builder.Property(v => v.ChangeReason).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(v => v.ChangedAtUtc).IsRequired();
        builder.Property(v => v.ChangedByActor).IsRequired();

        builder.Property(v => v.CreatedBy).IsRequired();
        builder.Property(v => v.RowVersion).IsRowVersion();

        builder.HasIndex(v => new { v.MemoryId, v.ChangedAtUtc });

        builder.HasOne<MemoryEntity>()
            .WithMany()
            .HasForeignKey(v => v.MemoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
