using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.FavoriteKnowledgeBase;

public sealed record FavoriteKnowledgeBaseCommand(Guid Id) : IRequest<KnowledgeBaseSummaryDto>;
