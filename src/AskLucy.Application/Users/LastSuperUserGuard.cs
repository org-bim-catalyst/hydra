using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Users;

/// <summary>
/// Shared FR-023 safety net for <c>LockUserCommandHandler</c>, <c>ChangeUserRoleCommandHandler</c>,
/// and <c>DeleteUserCommandHandler</c>: none of them may strand the system with zero active
/// Super Users. Distinct from FR-022's self-action guard, which only covers self-targeting —
/// this covers the *last remaining* Super User regardless of who targets them.
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
        if (activeSuperUserCount <= 1)
        {
            throw new UnauthorizedAccessException("Cannot remove the last remaining Super User.");
        }
    }
}
