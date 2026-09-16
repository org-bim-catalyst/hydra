using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// Mechanical single-role-per-user replacement (research.md Decision 7/8): a user's
/// <c>AspNetUserRoles</c> row is replaced, never added to, in one commit alongside the audit
/// row, a <c>SecurityStamp</c> bump, and an authorization-cache eviction. Business rules —
/// privileged-role restriction (FR-016), the last-Super-User safeguard (FR-017) — are the
/// caller's (Application command handler's) responsibility; this repository does not know
/// which roles are privileged.
/// </summary>
public sealed class RoleAssignmentRepository(
    AskLucyDbContext dbContext,
    IRoleAuditLogRepository auditLog,
    IAuthorizationCacheInvalidator cacheInvalidator) : IRoleAssignmentRepository
{
    public async Task<(IReadOnlyList<RoleAssignmentRecord> Items, int TotalCount)> SearchAsync(
        string? search, string? roleIdFilter, bool noRoleFilter, bool assignableOnly,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query =
            from user in dbContext.Users
            join userRole in dbContext.UserRoles on user.Id equals userRole.UserId into userRoles
            from userRole in userRoles.DefaultIfEmpty()
            join role in dbContext.Roles on userRole.RoleId equals role.Id into roles
            from role in roles.DefaultIfEmpty()
            select new { user, role };

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.user.Email!.Contains(search) ||
                (x.user.FirstName != null && x.user.FirstName.Contains(search)) ||
                (x.user.LastName != null && x.user.LastName.Contains(search)));
        }

        if (noRoleFilter)
        {
            query = query.Where(x => x.role == null);
        }
        else if (!string.IsNullOrWhiteSpace(roleIdFilter))
        {
            query = query.Where(x => x.role != null && x.role.Id == roleIdFilter);
        }

        if (assignableOnly)
        {
            query = query.Where(x => !(x.user.LockoutEnd != null && x.user.LockoutEnd > DateTimeOffset.UtcNow));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page1 = await query
            .OrderBy(x => x.user.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new RoleAssignmentRecord(
                x.user.Id, x.user.Email!, x.user.FirstName, x.user.LastName,
                x.user.LockoutEnd != null && x.user.LockoutEnd > DateTimeOffset.UtcNow,
                x.role != null ? x.role.Id : null,
                x.role != null ? x.role.Name : null,
                x.role != null && x.role.IsBuiltIn))
            .ToListAsync(cancellationToken);

        return (page1, totalCount);
    }

    public async Task<string?> GetCurrentRoleIdAsync(string userId, CancellationToken cancellationToken = default) =>
        (await dbContext.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == userId, cancellationToken))?.RoleId;

    public async Task<IReadOnlyList<string>> ListUserIdsByRoleAsync(string roleId, CancellationToken cancellationToken = default) =>
        await dbContext.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).ToListAsync(cancellationToken);

    public async Task<ReplaceRoleOutcome> ReplaceRoleAsync(
        string userId, string? roleId, string? expectedCurrentRoleId, string actorUserId, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return ReplaceRoleOutcome.UserNotFound;
        }

        if (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            return ReplaceRoleOutcome.UserLocked;
        }

        var current = await dbContext.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == userId, cancellationToken);
        if (current?.RoleId != expectedCurrentRoleId)
        {
            return ReplaceRoleOutcome.ConcurrencyMismatch;
        }

        string? oldRoleName = null;
        if (current is not null)
        {
            oldRoleName = (await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == current.RoleId, cancellationToken))?.Name;
            dbContext.UserRoles.Remove(current);
        }

        string? newRoleName = null;
        if (roleId is not null)
        {
            var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
            if (role is null)
            {
                return ReplaceRoleOutcome.RoleNotFound;
            }

            newRoleName = role.Name;
            dbContext.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = roleId });
        }

        user.SecurityStamp = Guid.NewGuid().ToString();

        var action = roleId is null ? RoleAuditAction.RoleRemoved : current is null ? RoleAuditAction.RoleAssigned : RoleAuditAction.RoleChanged;
        auditLog.Add(RoleAuditLog.Record(
            action, actorUserId, roleId ?? current?.RoleId, newRoleName ?? oldRoleName, userId,
            $$"""{"before":{{Json(oldRoleName)}},"after":{{Json(newRoleName)}}}"""));

        await dbContext.SaveChangesAsync(cancellationToken);
        cacheInvalidator.Evict(userId);

        return ReplaceRoleOutcome.Success;
    }

    public async Task<BulkAssignResult> BulkReplaceRoleAsync(
        string roleId, IReadOnlyList<string> userIds, string actorUserId, CancellationToken cancellationToken = default)
    {
        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null)
        {
            return new BulkAssignResult(0, [.. userIds.Select(id => (id, BulkAssignSkipReason.UserNotFound))]);
        }

        var users = await dbContext.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);
        var currentAssignments = await dbContext.UserRoles.Where(ur => userIds.Contains(ur.UserId)).ToDictionaryAsync(ur => ur.UserId, cancellationToken);

        var skipped = new List<(string UserId, BulkAssignSkipReason Reason)>();
        var assignedCount = 0;

        foreach (var userId in userIds.Distinct())
        {
            if (!users.TryGetValue(userId, out var user))
            {
                skipped.Add((userId, BulkAssignSkipReason.UserNotFound));
                continue;
            }

            if (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow)
            {
                skipped.Add((userId, BulkAssignSkipReason.UserLocked));
                continue;
            }

            if (currentAssignments.TryGetValue(userId, out var existing))
            {
                if (existing.RoleId == roleId)
                {
                    skipped.Add((userId, BulkAssignSkipReason.AlreadyAssigned));
                    continue;
                }

                dbContext.UserRoles.Remove(existing);
            }

            dbContext.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = roleId });
            user.SecurityStamp = Guid.NewGuid().ToString();

            auditLog.Add(RoleAuditLog.Record(RoleAuditAction.RoleAssigned, actorUserId, roleId, role.Name, userId, $"{{\"after\":{Json(role.Name)}}}"));
            assignedCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var userId in userIds.Distinct())
        {
            if (skipped.All(s => s.UserId != userId))
            {
                cacheInvalidator.Evict(userId);
            }
        }

        return new BulkAssignResult(assignedCount, skipped);
    }

    public async Task<IReadOnlyList<string>> ListEligibleIdsAsync(
        string roleId, string? search, bool actorIsSuperUser, CancellationToken cancellationToken = default)
    {
        var query =
            from user in dbContext.Users
            join userRole in dbContext.UserRoles on user.Id equals userRole.UserId into userRoles
            from userRole in userRoles.DefaultIfEmpty()
            join role in dbContext.Roles on userRole.RoleId equals role.Id into roles
            from role in roles.DefaultIfEmpty()
            select new { user, role };

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.user.Email!.Contains(search) ||
                (x.user.FirstName != null && x.user.FirstName.Contains(search)) ||
                (x.user.LastName != null && x.user.LastName.Contains(search)));
        }

        query = query.Where(x => !(x.user.LockoutEnd != null && x.user.LockoutEnd > DateTimeOffset.UtcNow));

        if (!actorIsSuperUser)
        {
            query = query.Where(x => x.role == null || !x.role.IsBuiltIn);
        }

        return await query.Select(x => x.user.Id).ToListAsync(cancellationToken);
    }

    private static string Json(string? value) => value is null ? "null" : $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
