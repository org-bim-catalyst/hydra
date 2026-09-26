using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.DeleteRole;

public sealed class DeleteRoleCommandHandler(
    IRoleRepository roleRepository, ICurrentUserAccessor currentUser, IAuthorizationCacheInvalidator cacheInvalidator)
    : IRequestHandler<DeleteRoleCommand, DeleteRoleResult>
{
    public async Task<DeleteRoleResult> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var existing = await roleRepository.GetByIdAsync(request.RoleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Role '{request.RoleId}' was not found.");

        if (existing.IsBuiltIn)
        {
            throw new UnauthorizedAccessException("Built-in roles cannot be deleted.");
        }

        SuperUserControlledPermissionGuard.EnsureCanDelete(currentUser, existing.Permissions.Keys);

        var affectedUserIds = await roleRepository.DeleteAsync(request.RoleId, request.ConcurrencyStamp, actorUserId, cancellationToken)
            ?? throw new KeyNotFoundException($"Role '{request.RoleId}' was not found.");

        foreach (var userId in affectedUserIds)
        {
            cacheInvalidator.Evict(userId);
        }

        return new DeleteRoleResult(affectedUserIds.Count);
    }
}
