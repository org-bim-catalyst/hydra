using MediatR;

namespace AskLucy.Application.Chats.Commands.PurgeUserChat;

/// <summary>
/// Permanently deletes a conversation (FR-004/FR-005) — irreversible from the user's
/// perspective, implemented as an immediate hard delete under the platform's existing
/// GDPR-style audited-erasure convention (constitution &#167;5). Distinct from the routine
/// (soft) delete in <c>DeleteUserChatCommand</c>.
/// </summary>
public sealed record PurgeUserChatCommand(Guid ChatId, bool Confirm) : IRequest;
