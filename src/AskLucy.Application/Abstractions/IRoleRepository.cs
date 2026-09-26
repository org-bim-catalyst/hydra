using AskLucy.Domain.Authorization;

namespace AskLucy.Application.Abstractions;

/// <summary>A role plus its permissions — for a built-in role, the effective set from <c>BuiltInRolePermissions</c> (data-model.md).</summary>
public sealed record RoleRecord(
    string Id,
    string Name,
    string? Description,
    bool IsBuiltIn,
    PermissionSet Permissions,
    int UserCount,
    DateTime? ModifiedAtUtc,
    string ConcurrencyStamp);

/// <summary>Aggregate-oriented access to roles and their permission grants (contracts/admin-roles-api.md §2).</summary>
public interface IRoleRepository
{
    Task<(IReadOnlyList<RoleRecord> Items, int TotalCount)> SearchAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<RoleRecord?> GetByIdAsync(string roleId, CancellationToken cancellationToken = default);

    Task<RoleRecord?> GetByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default);

    /// <summary>Creates a role. Returns <see langword="null"/> if <paramref name="name"/> is already taken (case-insensitive).</summary>
    Task<RoleRecord?> CreateAsync(string name, string? description, PermissionSet permissions, string actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a custom role's name/description/permissions. Returns <see langword="null"/> if
    /// <paramref name="roleId"/> doesn't exist or is built-in, and throws when
    /// <paramref name="expectedConcurrencyStamp"/> is stale — callers translate that to a 409
    /// (contracts/admin-roles-api.md §2).
    /// </summary>
    Task<RoleRecord?> UpdateAsync(
        string roleId, string name, string? description, PermissionSet permissions,
        string expectedConcurrencyStamp, string actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom role and unassigns every holder in one commit. Returns the unassigned user ids, or <see langword="null"/> if not found/built-in.</summary>
    Task<IReadOnlyList<string>?> DeleteAsync(
        string roleId, string expectedConcurrencyStamp, string actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Every role (built-in included) that includes <paramref name="permissionKey"/> — Permissions screen, FR-030.</summary>
    Task<IReadOnlyList<RoleRecord>> ListByPermissionAsync(string permissionKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids of every custom (non-built-in) role matching <paramref name="search"/>, unpaged
    /// (specs/056-bulk-select-all) — unless <paramref name="actorIsSuperUser"/>, excluding roles
    /// that carry a Super-User-controlled key (specs/074 FR-016j).
    /// </summary>
    Task<IReadOnlyList<string>> ListEligibleIdsAsync(string? search, bool actorIsSuperUser, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-delete variant of <see cref="DeleteAsync"/> — no client-supplied concurrency stamp
    /// (a bulk selection carries only ids, not per-row stamps read moments earlier); reads the
    /// row fresh and deletes it. Returns the unassigned user ids, or <see langword="null"/> if
    /// not found/built-in.
    /// </summary>
    Task<IReadOnlyList<string>?> DeleteByIdAsync(string roleId, string actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces only the <see cref="AdminPermissionCatalog.SuperUserControlledKeys"/> grants on a role
    /// (built-in included — the one write a built-in role accepts, specs/074 research D14) and
    /// writes a <c>RoleUpdated</c> audit row. Callers must have checked the actor is a Super User.
    /// Returns <see langword="null"/> if the role doesn't exist; throws on a non-controlled key.
    /// </summary>
    Task<RoleRecord?> SetControlledGrantsAsync(
        string roleId, IReadOnlyCollection<string> controlledKeys, string actorUserId, CancellationToken cancellationToken = default);
}
