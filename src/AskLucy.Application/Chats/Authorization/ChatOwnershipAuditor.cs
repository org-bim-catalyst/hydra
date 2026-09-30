using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Chats;

namespace AskLucy.Application.Chats.Authorization;

/// <summary>
/// specs/079 — the ownership check for the site-outline endpoints and capabilities. Same answer as
/// <see cref="ChatOwnershipGuard"/> (a missing chat and someone else's chat are indistinguishable),
/// but a chat that exists and belongs to someone else also writes an
/// <see cref="RoleAuditAction.AuthorizationDenied"/> row to the role audit trail (constitution &#167;8:
/// authorization denials MUST be audited). The notification audit trail (spec 067) is for
/// notification administration and is deliberately not used here.
/// </summary>
public sealed class ChatOwnershipAuditor(IRoleAuditLogRepository auditLog, IUnitOfWork unitOfWork)
{
    public async Task<UserChat> EnsureOwnedByAsync(
        UserChat? chat, string userId, string operation, CancellationToken cancellationToken = default)
    {
        if (chat is not null && !chat.IsOwnedBy(userId))
        {
            var detailsJson = JsonSerializer.Serialize(new { chatId = chat.Id, operation });
            auditLog.Add(RoleAuditLog.Record(RoleAuditAction.AuthorizationDenied, userId, detailsJson: detailsJson));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return ChatOwnershipGuard.EnsureOwnedBy(chat, userId);
    }
}
