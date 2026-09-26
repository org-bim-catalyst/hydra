using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;

namespace AskLucy.Application.Authorization;

/// <summary>
/// specs/074 FR-016h–j (research D14) — only a Super User may grant, remove, assign, unassign or
/// delete anything carrying an <see cref="AdminPermissionCatalog.SuperUserControlledKeys"/> key
/// (today just <em>View user content</em>). Every role create/update/delete and every assignment
/// path calls this before writing; a Super User passes straight through. An edit that leaves a
/// stored grant exactly as it was isn't changing it, so an Administrator can still rename such a role.
///
/// The built-in roles are already Super-User-only to assign, replace or change (FR-016, enforced by
/// the callers and by <c>BulkReplaceRoleAsync</c>'s per-row skip), so only custom roles are checked here.
/// </summary>
internal static class SuperUserControlledPermissionGuard
{
    public const string RefusalMessage = "Only a Super User can grant or remove View user content.";

    public static void EnsureIsSuperUser(ICurrentUserAccessor actor)
    {
        if (!IsSuperUser(actor))
        {
            throw new SuperUserRequiredException(RefusalMessage);
        }
    }

    public static void EnsureCanChangeRolePermissions(
        ICurrentUserAccessor actor, IEnumerable<string> storedKeys, IEnumerable<string> requestedKeys)
    {
        // Added or omitted both count (FR-016i) — the set of controlled keys must come back unchanged.
        if (!IsSuperUser(actor) && !Controlled(storedKeys).SetEquals(Controlled(requestedKeys)))
        {
            throw new SuperUserRequiredException(RefusalMessage);
        }
    }

    public static void EnsureCanAssignOrRemove(
        ICurrentUserAccessor actor, IEnumerable<string> roleKeysBeingAssigned, IEnumerable<string> roleKeysBeingRemoved)
    {
        if (!IsSuperUser(actor) && (Controlled(roleKeysBeingAssigned).Count > 0 || Controlled(roleKeysBeingRemoved).Count > 0))
        {
            throw new SuperUserRequiredException(RefusalMessage);
        }
    }

    public static void EnsureCanDelete(ICurrentUserAccessor actor, IEnumerable<string> roleKeys) =>
        EnsureCanAssignOrRemove(actor, [], roleKeys);

    /// <summary>
    /// One user's role replaced by <paramref name="newRole"/> (or removed when <see langword="null"/>).
    /// Re-assigning the role the user already holds changes nothing, so it passes.
    /// </summary>
    public static async Task EnsureCanReplaceRoleAsync(
        ICurrentUserAccessor actor,
        IRoleRepository roleRepository,
        IRoleAssignmentRepository assignmentRepository,
        string userId,
        RoleRecord? newRole,
        CancellationToken cancellationToken)
    {
        if (IsSuperUser(actor))
        {
            return;
        }

        var currentRoleId = await assignmentRepository.GetCurrentRoleIdAsync(userId, cancellationToken);
        if (string.IsNullOrEmpty(currentRoleId))
        {
            currentRoleId = null;
        }

        if (currentRoleId == newRole?.Id)
        {
            return;
        }

        var currentRole = currentRoleId is null ? null : await roleRepository.GetByIdAsync(currentRoleId, cancellationToken);
        EnsureCanAssignOrRemove(actor, CustomRoleKeys(newRole), CustomRoleKeys(currentRole));
    }

    /// <summary>All-or-nothing: one key-holding role in the batch refuses the whole delete (FR-016j).</summary>
    public static async Task EnsureCanDeleteAllAsync(
        ICurrentUserAccessor actor, IRoleRepository roleRepository, IReadOnlyCollection<string> roleIds, CancellationToken cancellationToken)
    {
        if (IsSuperUser(actor))
        {
            return;
        }

        var holders = await ListCustomHolderRoleIdsAsync(roleRepository, cancellationToken);
        if (roleIds.Any(holders.Contains))
        {
            throw new SuperUserRequiredException(RefusalMessage);
        }
    }

    /// <summary>All-or-nothing: refused if the role holds a controlled key, or if any target user's current role does.</summary>
    public static async Task EnsureCanBulkAssignAsync(
        ICurrentUserAccessor actor,
        IRoleRepository roleRepository,
        IRoleAssignmentRepository assignmentRepository,
        string roleId,
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken)
    {
        if (IsSuperUser(actor))
        {
            return;
        }

        var holders = await ListCustomHolderRoleIdsAsync(roleRepository, cancellationToken);
        if (holders.Contains(roleId))
        {
            throw new SuperUserRequiredException(RefusalMessage);
        }

        var targets = userIds.ToHashSet(StringComparer.Ordinal);
        foreach (var holderRoleId in holders)
        {
            var holderUserIds = await assignmentRepository.ListUserIdsByRoleAsync(holderRoleId, cancellationToken);
            if (holderUserIds.Any(targets.Contains))
            {
                throw new SuperUserRequiredException(RefusalMessage);
            }
        }
    }

    private static bool IsSuperUser(ICurrentUserAccessor actor) => actor.IsInRole(PrivilegedRoleNames.SuperUser);

    private static HashSet<string> Controlled(IEnumerable<string> keys) =>
        keys.Where(AdminPermissionCatalog.SuperUserControlledKeys.Contains).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> CustomRoleKeys(RoleRecord? role) =>
        role is null || role.IsBuiltIn ? [] : role.Permissions.Keys;

    private static async Task<HashSet<string>> ListCustomHolderRoleIdsAsync(IRoleRepository roleRepository, CancellationToken cancellationToken)
    {
        var roleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in AdminPermissionCatalog.SuperUserControlledKeys)
        {
            foreach (var role in await roleRepository.ListByPermissionAsync(key, cancellationToken))
            {
                if (!role.IsBuiltIn)
                {
                    roleIds.Add(role.Id);
                }
            }
        }

        return roleIds;
    }
}
