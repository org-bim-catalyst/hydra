using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.RestoreKnowledgeBase;

/// <summary>
/// Restores a knowledge base — from soft-deleted (cancels the pending automatic purge, FR-036
/// edge case) or from Archived (US3) back to Active, whichever applies (see
/// <c>KnowledgeBase.Restore</c>'s doc comment, research.md Decision 2). Needed by US1's own
/// quickstart scenario (restore-cancels-purge), not deferred to US3 despite un-archiving being
/// US3's own primary scenario — one command/endpoint serves both call sites.
/// </summary>
public sealed record RestoreKnowledgeBaseCommand(Guid Id) : IRequest<KnowledgeBaseSummaryDto>;
