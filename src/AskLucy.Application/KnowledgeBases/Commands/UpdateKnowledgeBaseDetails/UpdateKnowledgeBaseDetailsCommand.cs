using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.UpdateKnowledgeBaseDetails;

/// <summary>Full-replace update (FR-003) — mirrors <c>SaveUserAiPreferenceCommand</c>'s convention: the caller always sends the complete desired state, not a field-level diff.</summary>
public sealed record UpdateKnowledgeBaseDetailsCommand(
    Guid Id,
    string Name,
    string? Description,
    string? Color,
    string? Icon,
    Guid? CategoryId,
    IReadOnlyList<string>? Tags,
    string? Notes) : IRequest<KnowledgeBaseSummaryDto>;
