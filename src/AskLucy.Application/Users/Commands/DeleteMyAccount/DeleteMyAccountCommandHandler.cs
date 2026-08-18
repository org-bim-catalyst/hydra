using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Users.Commands.DeleteMyAccount;

/// <summary>
/// Requires the current password as re-confirmation before an irreversible account deletion.
///
/// <para>spec.md FR-026, research.md Decision 19 (added during <c>/speckit-analyze</c>
/// remediation, finding C2) — <see cref="IIdentityService.DeleteAsync"/> hard-deletes the
/// <c>ApplicationUser</c> row, which the <c>Memory</c>/<c>MemoryPreference</c>/
/// <c>MemoryCategoryPreference</c>/<c>Project</c> tables already cascade-delete from directly
/// (the same `ApplicationUser`-rooted FK cascade `UserChats` already relies on — see
/// `MemoryConfiguration`/`ProjectConfiguration`'s doc comments), so no extra code purges those.
/// <c>MemoryAuditLog</c>/<c>MemoryNotification</c> are deliberately *not* FK'd to
/// <c>ApplicationUser</c> (their own doc comments explain why — the audit trail must survive a
/// hard-purged memory), so they need an explicit anonymization step here instead, run before the
/// user row itself is deleted while <paramref name="request"/>'s caller is still known-valid.
/// </para>
/// </summary>
public sealed class DeleteMyAccountCommandHandler(
    IIdentityService identityService, IMemoryAuditLogRepository memoryAuditLogRepository,
    IMemoryNotificationRepository memoryNotificationRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<DeleteMyAccountCommand, IdentityOperationResult>
{
    public async Task<IdentityOperationResult> Handle(DeleteMyAccountCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var passwordValid = await identityService.VerifyPasswordAsync(userId, request.Password, cancellationToken);
        if (!passwordValid)
        {
            return new IdentityOperationResult(IdentityResultStatus.Failed, Errors: ["Incorrect password."]);
        }

        await memoryAuditLogRepository.AnonymizeUserAsync(userId, cancellationToken);
        await memoryNotificationRepository.AnonymizeUserAsync(userId, cancellationToken);

        await identityService.DeleteAsync(userId, cancellationToken);
        return new IdentityOperationResult(IdentityResultStatus.Success, userId);
    }
}
