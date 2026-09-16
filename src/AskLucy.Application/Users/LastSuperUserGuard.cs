using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Common;

namespace AskLucy.Application.Users;

/// <summary>
/// Shared FR-023 safety net for <c>LockUserCommandHandler</c>,
/// <c>AssignRoleCommandHandler</c> (which <c>ChangeUserRoleCommandHandler</c> delegates to),
/// and <c>DeleteUserCommandHandler</c>: none of them may strand the system with zero active
/// Super Users. Distinct from FR-022's self-action guard, which only covers self-targeting —
/// this covers the *last remaining* Super User regardless of who targets them.
///
/// Threshold logic itself lives in the pure <see cref="SuperUserSafeguard"/> domain service
/// (research.md Decision 8) — this class only loads the counts and translates that service's
/// <see cref="DomainRuleViolationException"/> into the <see cref="UnauthorizedAccessException"/>
/// this rule has always surfaced as (a 403, matching FR-023's existing, unchanged API contract).
/// </summary>
internal static class LastSuperUserGuard
{
    public static async Task EnsureNotStrandingSystemAsync(
        IIdentityService identityService,
        string targetUserId,
        bool actionRemovesTargetsSuperUserStatus,
        CancellationToken cancellationToken)
    {
        if (!actionRemovesTargetsSuperUserStatus)
        {
            return;
        }

        var targetRoles = await identityService.GetRolesAsync(targetUserId, cancellationToken);
        if (!targetRoles.Contains(PrivilegedRoleNames.SuperUser))
        {
            return;
        }

        var activeSuperUserCount = await identityService.CountActiveSuperUsersAsync(cancellationToken);

        try
        {
            SuperUserSafeguard.EnsureAtLeastOneActiveSuperUserRemains(activeSuperUserCount, superUsersBeingRemoved: 1);
        }
        catch (DomainRuleViolationException)
        {
            throw new UnauthorizedAccessException("Cannot remove the last remaining Super User.");
        }
    }
}
