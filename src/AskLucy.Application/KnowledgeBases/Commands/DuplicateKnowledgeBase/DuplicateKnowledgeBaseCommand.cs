using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.DuplicateKnowledgeBase;

/// <summary>Deep-copies a knowledge base — folder tree plus an independent physical file copy per document (FR-032/FR-037, research.md Decision 4). The source is unchanged.</summary>
public sealed record DuplicateKnowledgeBaseCommand(Guid Id) : IRequest<KnowledgeBaseSummaryDto>;
