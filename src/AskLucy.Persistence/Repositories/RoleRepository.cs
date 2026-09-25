using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Common;
using AskLucy.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// Roles live in <c>AspNetRoles</c>; a role's permission grants live in <c>AspNetRoleClaims</c>
/// with <see cref="PermissionClaims.Type"/> (research.md Decision 1/1b). Built-in roles carry no
/// claim rows except the Super-User-controlled grants on Administrator (specs/074 research D14);
/// their effective permissions come from <see cref="BuiltInRolePermissions"/> (Decision 2).
/// </summary>
public sealed class RoleRepository(AskLucyDbContext dbContext, IRoleAuditLogRepository auditLog) : IRoleRepository
{
    public async Task<(IReadOnlyList<RoleRecord> Items, int TotalCount)> SearchAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Roles.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r => r.Name != null && r.Name.Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Built-in roles always sort first (data-model.md "Roles" contract).
        var roles = await query
            .OrderByDescending(r => r.IsBuiltIn)
            .ThenBy(r => r.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = new List<RoleRecord>(roles.Count);
        foreach (var role in roles)
        {
            items.Add(await ToRecordAsync(role, cancellationToken));
        }

        return (items, totalCount);
    }

    public async Task<RoleRecord?> GetByIdAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        return role is null ? null : await ToRecordAsync(role, cancellationToken);
    }

    public async Task<RoleRecord?> GetByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.NormalizedName == normalizedName, cancellationToken);
        return role is null ? null : await ToRecordAsync(role, cancellationToken);
    }

    public async Task<RoleRecord?> CreateAsync(
        string name, string? description, PermissionSet permissions, string actorUserId, CancellationToken cancellationToken = default)
    {
        var normalizedName = name.ToUpperInvariant();
        var exists = await dbContext.Roles.AnyAsync(r => r.NormalizedName == normalizedName, cancellationToken);
        if (exists)
        {
            throw new DuplicateResourceException($"A role named '{name}' already exists.");
        }

        var now = DateTime.UtcNow;
        var role = new ApplicationRole(name)
        {
            Id = Guid.CreateVersion7().ToString(),
            NormalizedName = normalizedName,
            Description = description,
            IsBuiltIn = false,
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            CreatedAtUtc = now,
            CreatedBy = actorUserId,
            ModifiedAtUtc = now,
            ModifiedBy = actorUserId,
        };

        dbContext.Roles.Add(role);
        AddPermissionClaims(role.Id, permissions);

        auditLog.Add(RoleAuditLog.Record(
            RoleAuditAction.RoleCreated, actorUserId, role.Id, role.Name,
            detailsJson: $"{{\"after\":{RoleJson(name, description, permissions.Keys)}}}"));

        await SaveOrThrowDuplicateAsync(name, cancellationToken);

        return new RoleRecord(role.Id, role.Name!, role.Description, false, permissions, 0, role.ModifiedAtUtc, role.ConcurrencyStamp!);
    }

    public async Task<RoleRecord?> UpdateAsync(
        string roleId, string name, string? description, PermissionSet permissions,
        string expectedConcurrencyStamp, string actorUserId, CancellationToken cancellationToken = default)
    {
        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null || role.IsBuiltIn)
        {
            return null;
        }

        var normalizedName = name.ToUpperInvariant();
        var nameTaken = await dbContext.Roles.AnyAsync(r => r.Id != roleId && r.NormalizedName == normalizedName, cancellationToken);
        if (nameTaken)
        {
            throw new DuplicateResourceException($"A role named '{name}' already exists.");
        }

        // Tells EF the row's ConcurrencyStamp was this value when the caller read it — if the
        // actual current value differs, the UPDATE matches zero rows and EF throws
        // DbUpdateConcurrencyException (already globally mapped to 409 Problem Details).
        dbContext.Entry(role).Property(r => r.ConcurrencyStamp).OriginalValue = expectedConcurrencyStamp;

        var beforeName = role.Name;
        var beforeDescription = role.Description;
        var beforeKeys = await dbContext.RoleClaims
            .Where(c => c.RoleId == roleId && c.ClaimType == PermissionClaims.Type)
            .Select(c => c.ClaimValue!)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        role.Name = name;
        role.NormalizedName = normalizedName;
        role.Description = description;
        role.ConcurrencyStamp = Guid.NewGuid().ToString();
        role.ModifiedAtUtc = now;
        role.ModifiedBy = actorUserId;

        var existingClaims = await dbContext.RoleClaims
            .Where(c => c.RoleId == roleId && c.ClaimType == PermissionClaims.Type)
            .ToListAsync(cancellationToken);
        dbContext.RoleClaims.RemoveRange(existingClaims);
        AddPermissionClaims(roleId, permissions);

        auditLog.Add(RoleAuditLog.Record(
            RoleAuditAction.RoleUpdated, actorUserId, roleId, name,
            detailsJson: $"{{\"before\":{RoleJson(beforeName, beforeDescription, beforeKeys)},\"after\":{RoleJson(name, description, permissions.Keys)}}}"));

        await dbContext.SaveChangesAsync(cancellationToken);

        var userCount = await dbContext.UserRoles.CountAsync(ur => ur.RoleId == roleId, cancellationToken);
        return new RoleRecord(role.Id, role.Name, role.Description, false, permissions, userCount, role.ModifiedAtUtc, role.ConcurrencyStamp);
    }

    public async Task<IReadOnlyList<string>?> DeleteAsync(
        string roleId, string expectedConcurrencyStamp, string actorUserId, CancellationToken cancellationToken = default)
    {
        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null || role.IsBuiltIn)
        {
            return null;
        }

        dbContext.Entry(role).Property(r => r.ConcurrencyStamp).OriginalValue = expectedConcurrencyStamp;

        var affectedUserIds = await dbContext.UserRoles
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .ToListAsync(cancellationToken);

        var claims = await dbContext.RoleClaims.Where(c => c.RoleId == roleId).ToListAsync(cancellationToken);
        dbContext.RoleClaims.RemoveRange(claims);

        var assignments = await dbContext.UserRoles.Where(ur => ur.RoleId == roleId).ToListAsync(cancellationToken);
        dbContext.UserRoles.RemoveRange(assignments);

        dbContext.Roles.Remove(role);

        auditLog.Add(RoleAuditLog.Record(
            RoleAuditAction.RoleDeleted, actorUserId, roleId, role.Name,
            detailsJson: $"{{\"unassignedUserCount\":{affectedUserIds.Count}}}"));

        await dbContext.SaveChangesAsync(cancellationToken);

        return affectedUserIds;
    }

    public async Task<IReadOnlyList<string>> ListEligibleIdsAsync(string? search, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Roles.Where(r => !r.IsBuiltIn);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r => r.Name != null && r.Name.Contains(search));
        }

        return await query.Select(r => r.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>?> DeleteByIdAsync(string roleId, string actorUserId, CancellationToken cancellationToken = default)
    {
        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null || role.IsBuiltIn)
        {
            return null;
        }

        var affectedUserIds = await dbContext.UserRoles
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .ToListAsync(cancellationToken);

        var claims = await dbContext.RoleClaims.Where(c => c.RoleId == roleId).ToListAsync(cancellationToken);
        dbContext.RoleClaims.RemoveRange(claims);

        var assignments = await dbContext.UserRoles.Where(ur => ur.RoleId == roleId).ToListAsync(cancellationToken);
        dbContext.UserRoles.RemoveRange(assignments);

        dbContext.Roles.Remove(role);

        auditLog.Add(RoleAuditLog.Record(
            RoleAuditAction.RoleDeleted, actorUserId, roleId, role.Name,
            detailsJson: $"{{\"unassignedUserCount\":{affectedUserIds.Count}}}"));

        await dbContext.SaveChangesAsync(cancellationToken);

        return affectedUserIds;
    }

    public async Task<IReadOnlyList<RoleRecord>> ListByPermissionAsync(string permissionKey, CancellationToken cancellationToken = default)
    {
        var customRoleIds = await dbContext.RoleClaims
            .Where(c => c.ClaimType == PermissionClaims.Type && c.ClaimValue == permissionKey)
            .Select(c => c.RoleId)
            .ToListAsync(cancellationToken);

        var roles = await dbContext.Roles
            .Where(r => r.IsBuiltIn || customRoleIds.Contains(r.Id))
            .OrderByDescending(r => r.IsBuiltIn)
            .ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var items = new List<RoleRecord>(roles.Count);
        foreach (var role in roles)
        {
            // A built-in Administrator without a Super-User-controlled grant doesn't hold that key.
            var record = await ToRecordAsync(role, cancellationToken);
            if (record.Permissions.Contains(permissionKey))
            {
                items.Add(record);
            }
        }

        return items;
    }

    public async Task<RoleRecord?> SetControlledGrantsAsync(
        string roleId, IReadOnlyCollection<string> controlledKeys, string actorUserId, CancellationToken cancellationToken = default)
    {
        var unknown = controlledKeys.Where(k => !AdminPermissionCatalog.SuperUserControlledKeys.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"Not a Super-User-controlled permission: {string.Join(", ", unknown)}", nameof(controlledKeys));
        }

        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null)
        {
            return null;
        }

        var existing = await dbContext.RoleClaims
            .Where(c => c.RoleId == roleId && c.ClaimType == PermissionClaims.Type)
            .ToListAsync(cancellationToken);
        var beforeKeys = existing.Select(c => c.ClaimValue!).ToList();

        var toRemove = existing
            .Where(c => AdminPermissionCatalog.SuperUserControlledKeys.Contains(c.ClaimValue!) && !controlledKeys.Contains(c.ClaimValue!))
            .ToList();
        var toAdd = controlledKeys.Distinct().Where(k => !beforeKeys.Contains(k)).ToList();

        if (toRemove.Count > 0 || toAdd.Count > 0)
        {
            dbContext.RoleClaims.RemoveRange(toRemove);
            foreach (var key in toAdd)
            {
                dbContext.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = roleId, ClaimType = PermissionClaims.Type, ClaimValue = key });
            }

            var afterKeys = beforeKeys.Except(toRemove.Select(c => c.ClaimValue!)).Concat(toAdd).ToList();
            auditLog.Add(RoleAuditLog.Record(
                RoleAuditAction.RoleUpdated, actorUserId, roleId, role.Name!,
                detailsJson: $"{{\"before\":{RoleJson(role.Name, role.Description, beforeKeys)},\"after\":{RoleJson(role.Name, role.Description, afterKeys)}}}"));

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await ToRecordAsync(role, cancellationToken);
    }

    private static string Json(string? value) => value is null ? "null" : $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string JoinJson(IEnumerable<string> values) => string.Join(",", values.Select(Json));

    private static string RoleJson(string? name, string? description, IEnumerable<string> permissionKeys) =>
        $"{{\"name\":{Json(name)},\"description\":{Json(description)},\"permissionKeys\":[{JoinJson(permissionKeys)}]}}";

    private void AddPermissionClaims(string roleId, PermissionSet permissions)
    {
        foreach (var key in permissions.Keys)
        {
            dbContext.RoleClaims.Add(new IdentityRoleClaim<string>
            {
                RoleId = roleId,
                ClaimType = PermissionClaims.Type,
                ClaimValue = key,
            });
        }
    }

    private async Task SaveOrThrowDuplicateAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueRoleNameViolation(ex))
        {
            throw new DuplicateResourceException($"A role named '{name}' already exists.");
        }
    }

    // Closes the narrow race between the pre-check above and this SaveChanges — RoleNameIndex
    // (the built-in unique index on AspNetRoles.NormalizedName) is the actual guarantee.
    private static bool IsUniqueRoleNameViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } sqlException &&
        sqlException.Message.Contains("RoleNameIndex", StringComparison.Ordinal);

    private async Task<RoleRecord> ToRecordAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        var userCount = await dbContext.UserRoles.CountAsync(ur => ur.RoleId == role.Id, cancellationToken);

        var storedKeys = await dbContext.RoleClaims
            .Where(c => c.RoleId == role.Id && c.ClaimType == PermissionClaims.Type)
            .Select(c => c.ClaimValue!)
            .ToListAsync(cancellationToken);

        if (role.IsBuiltIn)
        {
            var builtInPermissions = BuiltInRolePermissions.For(role.Name!, storedKeys);
            return new RoleRecord(role.Id, role.Name!, role.Description, true, builtInPermissions, userCount, role.ModifiedAtUtc, role.ConcurrencyStamp!);
        }

        // Defensive against a not-yet-reconciled retired key (PermissionCatalogReconciler is the
        // primary cleanup — research.md Decision 10); never surface or persist an unknown key.
        var liveKeys = storedKeys.Where(key => AdminPermissionCatalog.TryGet(key, out _)).ToList();
        var permissions = liveKeys.Count == 0 ? PermissionSet.Empty : PermissionSet.Create(liveKeys);
        return new RoleRecord(role.Id, role.Name!, role.Description, false, permissions, userCount, role.ModifiedAtUtc, role.ConcurrencyStamp!);
    }
}
