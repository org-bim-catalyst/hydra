using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.CreateKnowledgeBase;

/// <summary>Creates a new knowledge base (FR-001) — saved with `Status: Draft` (FR-002).</summary>
public sealed record CreateKnowledgeBaseCommand(
    string Name,
    string? Description,
    string? Color,
    string? Icon,
    Guid? CategoryId,
    IReadOnlyList<string>? Tags) : IRequest<KnowledgeBaseSummaryDto>;
