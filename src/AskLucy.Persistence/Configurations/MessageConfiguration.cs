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
        builder.Property(m => m.Provider).HasMaxLength(50);
        builder.Property(m => m.Model).HasMaxLength(100);
        builder.Property(m => m.GenerationParametersJson);
        builder.Property(m => m.InputTokenCount);
        builder.Property(m => m.OutputTokenCount);

        builder.Property(m => m.CreatedBy).IsRequired();
        builder.Property(m => m.RowVersion).IsRowVersion();

        builder.HasQueryFilter(m => m.DeletedAtUtc == null);

        builder.HasIndex(m => new { m.UserChatId, m.CreatedAtUtc });

        builder.HasOne<UserChat>()
            .WithMany()
            .HasForeignKey(m => m.UserChatId)
            .OnDelete(DeleteBehavior.Cascade);

        // Attachments/Citations are children of this aggregate — reachable only via this
        // navigation (backed by the private field, since both are read-only collections),
        // never their own DbSet (constitution §5).
        builder.HasMany(m => m.Attachments)
            .WithOne()
            .HasForeignKey(a => a.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(m => m.Citations)
            .WithOne()
            .HasForeignKey(c => c.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.Citations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
