using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.UnpinKnowledgeBase;

public sealed record UnpinKnowledgeBaseCommand(Guid Id) : IRequest<KnowledgeBaseSummaryDto>;
