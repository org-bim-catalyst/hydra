using AskLucy.Domain.Chats;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="UserChat"/> — persistence mapping lives entirely here,
/// never as attributes on the Domain entity (constitution &#167;3). Migrates the legacy
/// int-keyed table onto the standard entity conventions (data-model.md, research.md Topic 5).
/// </summary>
public sealed class UserChatConfiguration : IEntityTypeConfiguration<UserChat>
{
    public void Configure(EntityTypeBuilder<UserChat> builder)
    {
        builder.ToTable("UserChats");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Title).IsRequired().HasMaxLength(200);
        builder.Property(c => c.SessionId).HasMaxLength(100);
        builder.Property(c => c.UserId).IsRequired();

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasQueryFilter(c => c.DeletedAtUtc == null);

        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.CreatedAtUtc);

        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.UserChats)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
