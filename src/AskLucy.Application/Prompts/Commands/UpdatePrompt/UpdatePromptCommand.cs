using AskLucy.Domain.Prompts;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.UpdatePrompt;

/// <summary>Edits a <see cref="Prompt"/>'s content/variables/model settings, creating a new <see cref="PromptVersion"/> (spec.md FR-030, contracts/prompts-api.md).</summary>
public sealed record UpdatePromptCommand(
    Guid Id,
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
    IReadOnlyList<PromptVariableDto> Variables,
    string? ChangeDescription) : IRequest<PromptDetailDto>;
