using MediatR;

namespace AskLucy.Application.Retrieval.Commands.UpdateConversationKnowledgeBases;

/// <summary>
/// Full-replace attach/detach of a conversation's knowledge bases (US1 T052; spec.md FR-035,
/// contracts/conversation-retrieval-api.md `PUT /api/v1/chats/{id}/knowledge-bases`). tasks.md
/// names this as two separate commands ("AttachKnowledgeBaseToConversation"/
/// "DetachKnowledgeBaseFromConversation"), but the actual documented contract and
/// <c>IConversationKnowledgeBaseRepository</c>'s <c>Add</c>/<c>RemoveExceptAsync</c> shape are
/// both a single full-replace operation — an empty <see cref="KnowledgeBaseIds"/> detaches every
/// knowledge base, a non-empty one replaces the attached set entirely. Applies to messages sent
/// after this call only (FR-035).
/// </summary>
public sealed record UpdateConversationKnowledgeBasesCommand(Guid ChatId, IReadOnlyList<Guid> KnowledgeBaseIds) : IRequest;
