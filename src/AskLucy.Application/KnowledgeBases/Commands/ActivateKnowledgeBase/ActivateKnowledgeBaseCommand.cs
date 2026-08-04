using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.ActivateKnowledgeBase;

/// <summary>Draft -&gt; Active (research.md Decision 1) — required before future RAG indexing eligibility (FR-006).</summary>
public sealed record ActivateKnowledgeBaseCommand(Guid Id) : IRequest<KnowledgeBaseSummaryDto>;
