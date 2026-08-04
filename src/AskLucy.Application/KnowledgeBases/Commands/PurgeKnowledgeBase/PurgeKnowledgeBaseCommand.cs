using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.PurgeKnowledgeBase;

/// <summary>Permanently deletes a soft-deleted knowledge base and cascades to permanently delete its documents' underlying files (FR-036) — irreversible; requires explicit confirmation.</summary>
public sealed record PurgeKnowledgeBaseCommand(Guid Id, bool Confirm) : IRequest;
