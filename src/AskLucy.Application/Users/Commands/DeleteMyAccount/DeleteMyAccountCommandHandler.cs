using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
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
/// <c>MemoryAuditLog</c> is deliberately *not* FK'd to <c>ApplicationUser</c> (its own doc comment
/// explains why — the audit trail must survive a hard-purged memory), so it needs an explicit
/// anonymization step here instead, run before the user row itself is deleted while
/// <paramref name="request"/>'s caller is still known-valid.
/// </para>
/// <para>specs/067 T098/T099 — the legacy <c>DocumentNotification</c>/<c>MemoryNotification</c>
/// inboxes moved onto the notification hub and carry no audit obligation, so their rows are simply
/// deleted (not anonymized) here, alongside an explicit, set-based purge of the hub's own
/// <c>Notification</c> rows (<see cref="INotificationRepository.DeleteAllForUserAsync"/>) rather
/// than relying solely on its FK cascade.</para>
/// </summary>
public sealed class DeleteMyAccountCommandHandler(
    IIdentityService identityService,
    IMemoryAuditLogRepository memoryAuditLogRepository,
    IMemoryNotificationRepository memoryNotificationRepository,
    IDocumentNotificationRepository documentNotificationRepository,
    INotificationRepository notificationRepository,
    ICurrentUserAccessor currentUser)
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
        await memoryNotificationRepository.DeleteAllForUserAsync(userId, cancellationToken);
        await documentNotificationRepository.DeleteAllForUserAsync(userId, cancellationToken);
        await notificationRepository.DeleteAllForUserAsync(userId, cancellationToken);

        await identityService.DeleteAsync(userId, cancellationToken);
        return new IdentityOperationResult(IdentityResultStatus.Success, userId);
    }
}
