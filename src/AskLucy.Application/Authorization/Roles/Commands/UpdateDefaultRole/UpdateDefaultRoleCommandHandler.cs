using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.UpdateDefaultRole;

public sealed class UpdateDefaultRoleCommandHandler(
    IRoleRepository roleRepository,
    IRoleAssignmentRepository roleAssignmentRepository,
    ICurrentUserAccessor currentUser,
    IAuthorizationCacheInvalidator cacheInvalidator) : IRequestHandler<UpdateDefaultRoleCommand, RoleSummaryDto>
{
    public async Task<RoleSummaryDto> Handle(UpdateDefaultRoleCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var existing = await roleRepository.GetByNormalizedNameAsync(DefaultRole.NormalizedName, cancellationToken);
        if (existing is not { IsDefault: true })
        {
            throw new KeyNotFoundException($"The built-in '{DefaultRole.Name}' role was not found.");
        }

        var requested = request.PermissionKeys.ToHashSet(StringComparer.Ordinal);

        var removedBaseline = DefaultRole.BaselinePermissionKeys.Where(key => !requested.Contains(key)).ToList();
        if (removedBaseline.Count > 0)
        {
            throw new DomainRuleViolationException(
                $"Basic permissions can't be removed from the {DefaultRole.Name} role: {string.Join(", ", removedBaseline)}.");
        }

        // Every account holds this role, so a controlled key here would let everyone read everyone's content.
        if (requested.Any(AdminPermissionCatalog.SuperUserControlledKeys.Contains))
        {
            throw new DomainRuleViolationException(
                $"View user content can't be granted to the {DefaultRole.Name} role, because every account holds it.");
        }

        var added = requested.Where(key => !DefaultRole.BaselinePermissionKeys.Contains(key)).ToList();
        var addedPermissions = added.Count == 0 ? PermissionSet.Empty : PermissionSet.Create(added);

        var updated = await roleRepository.UpdateDefaultRoleAsync(
            request.Description, addedPermissions, request.ConcurrencyStamp, actorUserId, cancellationToken)
            ?? throw new KeyNotFoundException($"The built-in '{DefaultRole.Name}' role was not found.");

        // Same as UpdateRoleCommandHandler (FR-006): every holder sees the change on their next request.
        var holderIds = await roleAssignmentRepository.ListUserIdsByRoleAsync(updated.Id, cancellationToken);
        foreach (var userId in holderIds)
        {
            cacheInvalidator.Evict(userId);
        }

        return RoleSummaryDto.Create(updated);
    }
}
