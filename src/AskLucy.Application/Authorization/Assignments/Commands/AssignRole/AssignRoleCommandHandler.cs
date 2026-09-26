using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Authorization.Assignments.Commands.AssignRole;

public sealed class AssignRoleCommandHandler(
    IRoleRepository roleRepository,
    IRoleAssignmentRepository assignmentRepository,
    IIdentityService identityService,
    ICurrentUserAccessor currentUser) : IRequestHandler<AssignRoleCommand>
{
    public async Task Handle(AssignRoleCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        RoleRecord? newRole = null;
        if (request.RoleId is not null)
        {
            newRole = await roleRepository.GetByIdAsync(request.RoleId, cancellationToken)
                ?? throw new KeyNotFoundException($"Role '{request.RoleId}' was not found.");
        }

        var newRoleName = newRole?.Name;

        // FR-016: only a Super User may grant/revoke Administrator or Super User itself, or
        // touch a target who currently holds either — mirrors ChangeUserRoleCommandHandler's
        // original two checks exactly (Clarifications 2026-07-28).
        var newRoleIsPrivileged = newRoleName is not null && PrivilegedRoleNames.All.Contains(newRoleName);
        if (newRoleIsPrivileged && !currentUser.IsInRole(PrivilegedRoleNames.SuperUser))
        {
            throw new UnauthorizedAccessException("Only a Super User can grant or revoke the Administrator or Super User role.");
        }

        var targetCurrentRoles = await identityService.GetRolesAsync(request.UserId, cancellationToken);
        var targetCurrentlyPrivileged = targetCurrentRoles.Any(PrivilegedRoleNames.All.Contains);
        if (targetCurrentlyPrivileged && !currentUser.IsInRole(PrivilegedRoleNames.SuperUser))
        {
            throw new UnauthorizedAccessException("Only a Super User can grant or revoke the Administrator or Super User role.");
        }

        // specs/074 FR-016j — ChangeUserRoleCommand delegates here, so this one call covers both endpoints.
        await SuperUserControlledPermissionGuard.EnsureCanReplaceRoleAsync(
            currentUser, roleRepository, assignmentRepository, request.UserId, newRole, cancellationToken);

        var actionRemovesSuperUserStatus =
            targetCurrentRoles.Contains(PrivilegedRoleNames.SuperUser) && newRoleName != PrivilegedRoleNames.SuperUser;
        await LastSuperUserGuard.EnsureNotStrandingSystemAsync(identityService, request.UserId, actionRemovesSuperUserStatus, cancellationToken);

        var outcome = await assignmentRepository.ReplaceRoleAsync(
            request.UserId, request.RoleId, request.ExpectedCurrentRoleId, actorUserId, cancellationToken);

        switch (outcome)
        {
            case ReplaceRoleOutcome.Success:
                return;
            case ReplaceRoleOutcome.UserNotFound:
                throw new KeyNotFoundException($"User '{request.UserId}' was not found.");
            case ReplaceRoleOutcome.RoleNotFound:
                throw new KeyNotFoundException($"Role '{request.RoleId}' was not found.");
            case ReplaceRoleOutcome.UserLocked:
                throw new DomainRuleViolationException("A locked user cannot be assigned a new role.");
            case ReplaceRoleOutcome.ConcurrencyMismatch:
            default:
                throw new ConcurrencyConflictException(
                    "This user's role was changed by another request. Please reload and try again.");
        }
    }
}
