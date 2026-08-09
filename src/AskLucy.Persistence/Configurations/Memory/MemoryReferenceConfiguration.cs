using AskLucy.Domain.Chats;
using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Memory;

/// <summary>
/// EF Core mapping for <see cref="MemoryReference"/>. <see cref="MemoryReference.ContentSnapshot"/>'s
/// encryption converter is applied in <c>AskLucyDbContext.OnModelCreating</c> (research.md
/// Decision 12). No FK on <see cref="MemoryReference.MemoryId"/> — the trace must remain resolvable
/// even after the source memory is edited/archived/deleted (data-model.md).
/// </summary>
public sealed class MemoryReferenceConfiguration : IEntityTypeConfiguration<MemoryReference>
{
    public void Configure(EntityTypeBuilder<MemoryReference> builder)
    {
        builder.ToTable("MemoryReferences");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.RelevanceScore).HasPrecision(5, 4).IsRequired();
        builder.Property(r => r.ContentSnapshot).IsRequired();

        builder.Property(r => r.CreatedBy).IsRequired();
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasIndex(r => r.MessageId);
        builder.HasIndex(r => r.MemoryId);

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(r => r.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
