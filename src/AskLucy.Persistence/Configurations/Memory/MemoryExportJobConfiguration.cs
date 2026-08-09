using AskLucy.Domain.Memory;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Memory;

/// <summary>EF Core mapping for <see cref="MemoryExportJob"/> (research.md Decision 14).</summary>
public sealed class MemoryExportJobConfiguration : IEntityTypeConfiguration<MemoryExportJob>
{
    public void Configure(EntityTypeBuilder<MemoryExportJob> builder)
    {
        builder.ToTable("MemoryExportJobs");

        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();

        builder.Property(j => j.UserId).IsRequired();
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(j => j.StoredFileName).HasMaxLength(500);
        builder.Property(j => j.FailureReason).HasMaxLength(1000);

        builder.Property(j => j.CreatedBy).IsRequired();
        builder.Property(j => j.RowVersion).IsRowVersion();

        builder.HasQueryFilter(j => j.DeletedAtUtc == null);

        builder.HasIndex(j => j.UserId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(j => j.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
