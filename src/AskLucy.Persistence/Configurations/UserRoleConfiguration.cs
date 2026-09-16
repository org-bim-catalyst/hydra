using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>A user holds at most one role (FR-012, research.md Decision 7) — enforced at the data layer, not only in Application handlers.</summary>
public sealed class UserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<string>> builder)
    {
        builder.HasIndex(ur => ur.UserId)
            .IsUnique()
            .HasDatabaseName("IX_AspNetUserRoles_UserId");
    }
}
