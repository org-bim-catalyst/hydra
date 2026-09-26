using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.SetAdministratorContentAccess;

/// <summary>
/// The one write the built-in Administrator role accepts (research D14): its Super-User-controlled
/// grants. <see cref="IRoleRepository.SetControlledGrantsAsync"/> writes the <c>RoleUpdated</c>
/// audit row (FR-016k); every Administrator's cached permissions are then evicted so the change
/// applies on their next request (FR-006).
/// </summary>
public sealed class SetAdministratorContentAccessCommandHandler(
    IRoleRepository roleRepository,
    IRoleAssignmentRepository roleAssignmentRepository,
    ICurrentUserAccessor currentUser,
    IAuthorizationCacheInvalidator cacheInvalidator)
    : IRequestHandler<SetAdministratorContentAccessCommand, AdministratorContentAccessDto>
{
    public async Task<AdministratorContentAccessDto> Handle(SetAdministratorContentAccessCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        SuperUserControlledPermissionGuard.EnsureIsSuperUser(currentUser);

        var administrator = await roleRepository.GetByNormalizedNameAsync(PrivilegedRoleNames.Administrator.ToUpperInvariant(), cancellationToken)
            ?? throw new KeyNotFoundException("The built-in Administrator role was not found.");

        // The role's effective set holds a controlled key only when it's stored, so this is the stored set.
        var controlledKeys = administrator.Permissions.Keys
            .Where(AdminPermissionCatalog.SuperUserControlledKeys.Contains)
            .ToHashSet(StringComparer.Ordinal);
        if (request.Granted)
        {
            controlledKeys.Add(AdminPermissionCatalog.OperationalFailuresContentView);
        }
        else
        {
            controlledKeys.Remove(AdminPermissionCatalog.OperationalFailuresContentView);
        }

        var updated = await roleRepository.SetControlledGrantsAsync(administrator.Id, controlledKeys, actorUserId, cancellationToken)
            ?? throw new KeyNotFoundException("The built-in Administrator role was not found.");

        var holderIds = await roleAssignmentRepository.ListUserIdsByRoleAsync(administrator.Id, cancellationToken);
        foreach (var userId in holderIds)
        {
            cacheInvalidator.Evict(userId);
        }

        return new AdministratorContentAccessDto(updated.Permissions.Contains(AdminPermissionCatalog.OperationalFailuresContentView));
    }
}
