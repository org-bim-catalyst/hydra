using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authorization;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.UpdateRole;

public sealed class UpdateRoleCommandHandler(
    IRoleRepository roleRepository,
    IRoleAssignmentRepository roleAssignmentRepository,
    ICurrentUserAccessor currentUser,
    IAuthorizationCacheInvalidator cacheInvalidator)
    : IRequestHandler<UpdateRoleCommand, RoleSummaryDto>
{
    public async Task<RoleSummaryDto> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var existing = await roleRepository.GetByIdAsync(request.RoleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Role '{request.RoleId}' was not found.");

        if (existing.IsBuiltIn)
        {
            throw new UnauthorizedAccessException("Built-in roles cannot be edited.");
        }

        var name = RoleName.From(request.Name);
        var permissions = PermissionSet.Create(request.PermissionKeys);
        SuperUserControlledPermissionGuard.EnsureCanChangeRolePermissions(currentUser, existing.Permissions.Keys, permissions.Keys);

        var updated = await roleRepository.UpdateAsync(
            request.RoleId, name.Value, request.Description, permissions, request.ConcurrencyStamp, actorUserId, cancellationToken)
            ?? throw new KeyNotFoundException($"Role '{request.RoleId}' was not found.");

        // Every current holder's effective permissions must reflect the change on their very next
        // request (FR-006), not wait out the 30s cache safety net.
        var holderIds = await roleAssignmentRepository.ListUserIdsByRoleAsync(request.RoleId, cancellationToken);
        foreach (var userId in holderIds)
        {
            cacheInvalidator.Evict(userId);
        }

        return RoleSummaryDto.Create(updated);
    }
}
