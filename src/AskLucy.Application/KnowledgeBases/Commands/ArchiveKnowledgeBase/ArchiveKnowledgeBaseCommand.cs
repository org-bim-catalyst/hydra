using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.ArchiveKnowledgeBase;

/// <summary>Active -&gt; Archived (FR-004) — removes the knowledge base from the default active dashboard view without deleting it.</summary>
public sealed record ArchiveKnowledgeBaseCommand(Guid Id) : IRequest<KnowledgeBaseSummaryDto>;
