using MediatR;

namespace AskLucy.Application.Chats.Queries.ExportUserChat;

/// <summary>Produces a structured, portable export of a conversation (FR-025).</summary>
public sealed record ExportUserChatQuery(Guid ChatId) : IRequest<ConversationExportDto>;
