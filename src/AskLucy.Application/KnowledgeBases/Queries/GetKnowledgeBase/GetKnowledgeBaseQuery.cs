using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.GetKnowledgeBase;

public sealed record GetKnowledgeBaseQuery(Guid Id) : IRequest<KnowledgeBaseDetailDto>;
