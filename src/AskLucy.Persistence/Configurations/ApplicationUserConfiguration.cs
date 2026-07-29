using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the admin-dashboard/user-management additions to
/// <see cref="ApplicationUser"/> (specs/001-admin-dashboard). The global query filter makes a
/// soft-deleted user transparently invisible to <see cref="Microsoft.AspNetCore.Identity.UserManager{TUser}"/>
/// and <c>UserAdminRepository</c> alike, since both read through <c>AskLucyDbContext.Users</c>
/// (research.md Topic 1).
/// </summary>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.HasQueryFilter(u => !u.IsDeleted);

        builder.HasIndex(u => u.CreatedAtUtc);
        builder.HasIndex(u => u.IsDeleted);
    }
}
