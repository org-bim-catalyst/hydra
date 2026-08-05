using MediatR;

namespace AskLucy.Application.Retrieval.Queries.GetConversationKnowledgeBases;

/// <summary>contracts/conversation-retrieval-api.md `GET /api/v1/chats/{id}/knowledge-bases`.</summary>
public sealed record GetConversationKnowledgeBasesQuery(Guid ChatId) : IRequest<IReadOnlyList<Guid>>;
