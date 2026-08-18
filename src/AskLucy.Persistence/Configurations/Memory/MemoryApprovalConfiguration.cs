using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Persistence.Configurations.Memory;

public sealed class MemoryApprovalConfiguration : IEntityTypeConfiguration<MemoryApproval>
{
    public void Configure(EntityTypeBuilder<MemoryApproval> builder)
    {
        builder.ToTable("MemoryApprovals");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Decision).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.DecidedByActor).HasMaxLength(450);

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => a.MemoryId);

        builder.HasOne<MemoryEntity>()
            .WithMany()
            .HasForeignKey(a => a.MemoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
