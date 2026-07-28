using AskLucy.Domain.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Message"/> — see UserChatConfiguration for the conventions this mirrors.</summary>
public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.SourceText);

        builder.Property(m => m.CreatedBy).IsRequired();
        builder.Property(m => m.RowVersion).IsRowVersion();

        builder.HasQueryFilter(m => m.DeletedAtUtc == null);

        builder.HasIndex(m => new { m.UserChatId, m.CreatedAtUtc });

        builder.HasOne<UserChat>()
            .WithMany()
            .HasForeignKey(m => m.UserChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
