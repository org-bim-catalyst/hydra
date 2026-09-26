using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Authorization.Roles.Commands.DuplicateRole;

public sealed class DuplicateRoleCommandHandler(IRoleRepository roleRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<DuplicateRoleCommand, RoleSummaryDto>
{
    public const string RefusalMessage = "Only a Super User can duplicate a role.";

    public async Task<RoleSummaryDto> Handle(DuplicateRoleCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        if (!currentUser.IsInRole(PrivilegedRoleNames.SuperUser))
        {
            throw new SuperUserRequiredException(RefusalMessage);
        }

        var source = await roleRepository.GetByIdAsync(request.SourceRoleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Role '{request.SourceRoleId}' was not found.");

        // The effective set, so a built-in role's copy holds what the role grants today.
        if (source.Permissions.IsEmpty)
        {
            throw new DomainRuleViolationException(
                $"'{source.Name}' has no permissions to copy. Add a permission to it first, or create a new role instead.");
        }

        var name = RoleName.From(request.Name);
        var created = await roleRepository.DuplicateAsync(
            source.Name, name.Value, request.Description, source.Permissions, actorUserId, cancellationToken);

        return RoleSummaryDto.Create(created);
    }
}
