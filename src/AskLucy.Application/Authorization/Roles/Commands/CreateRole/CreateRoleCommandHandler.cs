using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authorization;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.CreateRole;

public sealed class CreateRoleCommandHandler(IRoleRepository roleRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<CreateRoleCommand, RoleSummaryDto>
{
    public async Task<RoleSummaryDto> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var name = RoleName.From(request.Name);
        var permissions = PermissionSet.Create(request.PermissionKeys);
        SuperUserControlledPermissionGuard.EnsureCanChangeRolePermissions(currentUser, [], permissions.Keys);

        var created = await roleRepository.CreateAsync(name.Value, request.Description, permissions, actorUserId, cancellationToken);
        return RoleSummaryDto.Create(created!);
    }
}
