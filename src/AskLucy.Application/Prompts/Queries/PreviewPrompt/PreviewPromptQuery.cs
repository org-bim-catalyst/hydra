using MediatR;

namespace AskLucy.Application.Prompts.Queries.PreviewPrompt;

/// <summary>Resolves a prompt's content with supplied/example/default variable values — no AI provider call is made (spec.md FR-005).</summary>
public sealed record PreviewPromptQuery(Guid Id, IReadOnlyDictionary<string, string?> VariableValues) : IRequest<PromptPreviewDto>;

public sealed record PromptPreviewDto(
    string? SystemInstructions,
    string? DeveloperInstructions,
    string UserInstructions,
    string? ContextText,
    string? OutputInstructions,
    string? Constraints);
