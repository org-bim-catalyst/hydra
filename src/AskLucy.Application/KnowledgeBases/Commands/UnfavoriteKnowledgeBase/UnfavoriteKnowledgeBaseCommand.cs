using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.UnfavoriteKnowledgeBase;

public sealed record UnfavoriteKnowledgeBaseCommand(Guid Id) : IRequest<KnowledgeBaseSummaryDto>;
