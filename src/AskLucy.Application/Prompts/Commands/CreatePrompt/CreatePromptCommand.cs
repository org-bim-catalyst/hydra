using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.CreatePrompt;

/// <summary>Creates a new <see cref="Prompt"/> and its first <see cref="PromptVersion"/> (spec.md FR-001-FR-005, FR-010-FR-012, contracts/prompts-api.md).</summary>
public sealed record CreatePromptCommand(
    string Name,
    string? Description,
    PromptType PromptType,
    string? SystemInstructions,
    string? DeveloperInstructions,
    string UserInstructions,
    string? ContextText,
    string? ExamplesText,
    string? OutputInstructions,
    string? Constraints,
    Guid? CategoryId,
    Guid? FolderId,
    PromptCapabilityRequirements RequiredCapabilities,
    string? PreferredModelKey,
    IReadOnlyList<PromptVariableDto> Variables) : IRequest<PromptDetailDto>;
