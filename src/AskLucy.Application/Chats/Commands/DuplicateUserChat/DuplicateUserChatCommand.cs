using MediatR;

namespace AskLucy.Application.Chats.Commands.DuplicateUserChat;

/// <summary>
/// Duplicates a conversation into a new, independent conversation containing a full copy of
/// the source's messages up to this point (FR-010 — a true branch/fork, per the
/// specs/002-chat-history-management clarification). The source conversation is unchanged.
/// </summary>
public sealed record DuplicateUserChatCommand(Guid ChatId) : IRequest<UserChatSummaryDto>;
