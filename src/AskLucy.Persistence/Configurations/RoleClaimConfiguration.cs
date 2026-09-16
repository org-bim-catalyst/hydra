using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>
/// <c>AspNetRoleClaims</c> ships with no unique constraint by default (research.md Decision 1b) —
/// this index prevents duplicate permission grants and detects a concurrent double-attach.
/// </summary>
public sealed class RoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<string>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<string>> builder)
    {
        builder.HasIndex(c => new { c.RoleId, c.ClaimType, c.ClaimValue })
            .IsUnique()
            .HasDatabaseName("IX_AspNetRoleClaims_RoleId_ClaimType_ClaimValue");
    }
}
