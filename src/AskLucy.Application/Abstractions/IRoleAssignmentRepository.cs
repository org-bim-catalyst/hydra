namespace AskLucy.Application.Abstractions;

public sealed record RoleAssignmentRecord(
    string UserId, string Email, string? FirstName, string? LastName, bool IsLockedOut,
    string? RoleId, string? RoleName, bool RoleIsBuiltIn);

public enum BulkAssignSkipReason
{
    PrivilegedRoleRequiresSuperUser,
    LastSuperUser,
    UserLocked,
    UserNotFound,
    AlreadyAssigned,
}

public sealed record BulkAssignResult(int AssignedCount, IReadOnlyList<(string UserId, BulkAssignSkipReason Reason)> Skipped);

public enum ReplaceRoleOutcome
{
    Success,
    UserNotFound,
    UserLocked,
    ConcurrencyMismatch,
    RoleNotFound,
}

/// <summary>
/// Replace-in-one-commit role assignment (research.md Decision 8) — a user holds at most one
/// role (FR-012), enforced here and by the <c>IX_AspNetUserRoles_UserId</c> unique index.
/// </summary>
public interface IRoleAssignmentRepository
{
    Task<(IReadOnlyList<RoleAssignmentRecord> Items, int TotalCount)> SearchAsync(
        string? search, string? roleIdFilter, bool noRoleFilter, bool assignableOnly,
        int page, int pageSize, CancellationToken cancellationToken = default);

    Task<string?> GetCurrentRoleIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Every user id currently holding <paramref name="roleId"/> — used to evict every holder's authorization cache the moment that role's permissions change (FR-006).</summary>
    Task<IReadOnlyList<string>> ListUserIdsByRoleAsync(string roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces <paramref name="userId"/>'s role with <paramref name="roleId"/> (or removes it when <see langword="null"/>),
    /// writes the audit row, bumps <c>SecurityStamp</c>, and evicts the user's authorization cache — all in one commit.
    /// The specific <see cref="ReplaceRoleOutcome"/> lets the caller map to the right status code
    /// (contracts/admin-roles-api.md §3: 404 not found, 400 locked, 409 concurrency mismatch).
    /// </summary>
    Task<ReplaceRoleOutcome> ReplaceRoleAsync(
        string userId, string? roleId, string? expectedCurrentRoleId, string actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Applies <paramref name="roleId"/> to every assignable id in <paramref name="userIds"/> in one commit; the rest are reported as skipped.</summary>
    Task<BulkAssignResult> BulkReplaceRoleAsync(
        string roleId, IReadOnlyList<string> userIds, string actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids of every user matching <paramref name="search"/> that <paramref name="roleId"/> could
    /// legally be assigned to right now (specs/056-bulk-select-all) — excludes locked users and,
    /// unless <paramref name="actorIsSuperUser"/>, users currently holding a built-in role,
    /// mirroring <c>AssignRoleCommandHandler</c>'s existing per-row checks.
    /// </summary>
    Task<IReadOnlyList<string>> ListEligibleIdsAsync(
        string roleId, string? search, bool actorIsSuperUser, CancellationToken cancellationToken = default);
}
