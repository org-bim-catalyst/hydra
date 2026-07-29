using AskLucy.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Users.Commands.DeleteUser;

public sealed class DeleteUserCommandHandler(
    IIdentityService identityService,
    IUserAdminRepository userAdminRepository,
    ICurrentUserAccessor currentUser,
    ILogger<DeleteUserCommandHandler> logger) : IRequestHandler<DeleteUserCommand>
{
    public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        if (string.Equals(request.UserId, actorUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("You cannot delete your own account.");
        }

        await LastSuperUserGuard.EnsureNotStrandingSystemAsync(
            identityService, request.UserId, actionRemovesTargetsSuperUserStatus: true, cancellationToken);

        var deleted = await userAdminRepository.DeleteAsync(request.UserId, actorUserId, cancellationToken);
        if (!deleted)
        {
            throw new KeyNotFoundException("User not found.");
        }

        AdminActionLog.AdminUserActionPerformed(logger, "Delete", actorUserId, request.UserId, "Account soft-deleted");
    }
}
