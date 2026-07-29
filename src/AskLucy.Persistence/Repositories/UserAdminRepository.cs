using System.Linq.Expressions;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// Projects directly to <see cref="UserAdminDto"/> via explicit LINQ (not AutoMapper's
/// <c>ProjectTo</c>) so the current-role column can be a correlated subquery against
/// <c>AspNetUserRoles</c>/<c>AspNetRoles</c> — a join AutoMapper's per-entity <c>MapFrom</c>
/// can't express without a navigation property.
/// </summary>
public sealed class UserAdminRepository(AskLucyDbContext dbContext) : IUserAdminRepository
{
    private Expression<Func<ApplicationUser, UserAdminDto>> ProjectToDto() => u => new UserAdminDto(
        u.Id,
        u.Email ?? string.Empty,
        u.FirstName,
        u.LastName,
        u.EmailConfirmed,
        u.TwoFactorEnabled,
        u.LockoutEnabled,
        u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow,
        (from ur in dbContext.UserRoles
         join r in dbContext.Roles on ur.RoleId equals r.Id
         where ur.UserId == u.Id && (r.Name == PrivilegedRoleNames.Administrator || r.Name == PrivilegedRoleNames.SuperUser)
         select r.Name!).FirstOrDefault() ?? PrivilegedRoleNames.Regular,
        u.CreatedAtUtc);

    public async Task<UserAdminDto?> GetByIdAsync(string userId, CancellationToken cancellationToken = default) =>
        await dbContext.Users.Where(u => u.Id == userId).Select(ProjectToDto()).FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> UpdateAsync(string userId, string? firstName, string? lastName, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        // Only these two fields are ever written from a PATCH request — the source
        // request body's other properties (id, passwordHash, roles, etc.) are never
        // even read this far up the stack. This is what closes the mass-assignment gap.
        user.FirstName = firstName;
        user.LastName = lastName;

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(string userId, string actorUserId, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        user.IsDeleted = true;
        user.DeletedAtUtc = DateTime.UtcNow;
        user.DeletedBy = actorUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PagedResult<UserAdminDto>> SearchAsync(
        string? search, string sortBy, bool sortDescending, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Parameterized via EF Core's translated .Contains (never string-interpolated
            // SQL — constitution §8); case-insensitivity follows the SQL Server default
            // collation's case-insensitive comparison.
            query = query.Where(u =>
                u.Email!.Contains(search) ||
                (u.FirstName != null && u.FirstName.Contains(search)) ||
                (u.LastName != null && u.LastName.Contains(search)));
        }

        query = sortBy switch
        {
            "createdAtUtc" => sortDescending ? query.OrderByDescending(u => u.CreatedAtUtc) : query.OrderBy(u => u.CreatedAtUtc),
            _ => sortDescending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
        };

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ProjectToDto())
            .ToListAsync(cancellationToken);

        return new PagedResult<UserAdminDto>(items, totalCount, page, pageSize);
    }
}
