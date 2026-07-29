using AskLucy.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users.Commands.ChangeUserRole;

public sealed class ChangeUserRoleCommandHandler(
    IIdentityService identityService,
    ICurrentUserAccessor currentUser,
    ILogger<ChangeUserRoleCommandHandler> logger) : IRequestHandler<ChangeUserRoleCommand>
{
    public async Task Handle(ChangeUserRoleCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var targetCurrentRoles = await identityService.GetRolesAsync(request.UserId, cancellationToken);
        var targetCurrentlyPrivileged = targetCurrentRoles.Any(PrivilegedRoleNames.All.Contains);
        var newRoleIsPrivileged = request.NewRole != PrivilegedRoleNames.Regular;

        // FR-014 / Clarifications 2026-07-28: only a Super User may grant or revoke
        // Administrator/Super User itself — a plain Administrator may still change a
        // regular user's role (neither side of the change is privileged).
        if ((newRoleIsPrivileged || targetCurrentlyPrivileged) && !currentUser.IsInRole(PrivilegedRoleNames.SuperUser))
        {
            throw new UnauthorizedAccessException("Only a Super User can grant or revoke the Administrator or Super User role.");
        }

        var actionRemovesSuperUserStatus =
            targetCurrentRoles.Contains(PrivilegedRoleNames.SuperUser) && request.NewRole != PrivilegedRoleNames.SuperUser;
        await LastSuperUserGuard.EnsureNotStrandingSystemAsync(
            identityService, request.UserId, actionRemovesSuperUserStatus, cancellationToken);

        var oldRole = targetCurrentRoles.FirstOrDefault(PrivilegedRoleNames.All.Contains) ?? PrivilegedRoleNames.Regular;
        await identityService.ChangeRoleAsync(request.UserId, request.NewRole, cancellationToken);

        AdminActionLog.AdminUserRoleChanged(logger, actorUserId, request.UserId, oldRole, request.NewRole);
    }
}
