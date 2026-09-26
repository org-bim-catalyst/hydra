using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization;
using AskLucy.Application.Authorization.Assignments.Commands.AssignRole;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users.Commands.ChangeUserRole;

/// <summary>
/// Legacy contract (<c>PATCH /users/{userId}/role</c>, deprecated in favor of
/// <c>PUT /admin/role-assignments/{userId}</c> — specs/055-role-management contracts §4)
/// preserved unchanged: still takes a role *name* (a built-in role name, or "Regular" — the legacy
/// name for the built-in User role) and still logs <see cref="AdminActionLog.AdminUserRoleChanged"/>. All of the actual
/// rule enforcement (FR-014/FR-016 privileged-role restriction, FR-023/FR-017 last-Super-User
/// safeguard) now lives once in <see cref="AssignRoleCommandHandler"/>, which this delegates to
/// — no duplicated business logic (research.md Decision 8, plan.md T085).
/// </summary>
public sealed class ChangeUserRoleCommandHandler(
    ISender mediator,
    IRoleRepository roleRepository,
    IRoleAssignmentRepository roleAssignmentRepository,
    IIdentityService identityService,
    ICurrentUserAccessor currentUser,
    ILogger<ChangeUserRoleCommandHandler> logger) : IRequestHandler<ChangeUserRoleCommand>
{
    public async Task Handle(ChangeUserRoleCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var targetCurrentRoles = await identityService.GetRolesAsync(request.UserId, cancellationToken);
        var oldRole = targetCurrentRoles.Count > 0 ? targetCurrentRoles[0] : DefaultRole.Name;

        // The legacy contract only ever names a built-in role — resolve to the built-in row's id
        // so AssignRoleCommand's generic (works-for-any-role) contract can be reused without change.
        var newRoleName = request.NewRole == PrivilegedRoleNames.Regular ? DefaultRole.Name : request.NewRole;
        var role = await roleRepository.GetByNormalizedNameAsync(newRoleName.ToUpperInvariant(), cancellationToken)
            ?? throw new KeyNotFoundException($"Role '{newRoleName}' was not found.");
        var newRoleId = role.Id;

        // No client-supplied concurrency token on this legacy endpoint — read fresh immediately
        // before delegating, same as this endpoint's pre-existing (unprotected) behavior.
        var expectedCurrentRoleId = await roleAssignmentRepository.GetCurrentRoleIdAsync(request.UserId, cancellationToken);

        await mediator.Send(new AssignRoleCommand(request.UserId, newRoleId, expectedCurrentRoleId), cancellationToken);

        AdminActionLog.AdminUserRoleChanged(logger, actorUserId, request.UserId, oldRole, request.NewRole);
    }
}
