using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.PinKnowledgeBase;

public sealed record PinKnowledgeBaseCommand(Guid Id) : IRequest<KnowledgeBaseSummaryDto>;
