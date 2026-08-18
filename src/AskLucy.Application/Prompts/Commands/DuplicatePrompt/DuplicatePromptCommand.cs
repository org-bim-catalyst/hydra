using MediatR;

namespace AskLucy.Application.Prompts.Commands.DuplicatePrompt;

/// <summary>Creates a new, independent prompt seeded from an existing one's current content, with its own fresh version-1 history (spec.md FR-001, spec.md Edge Cases).</summary>
public sealed record DuplicatePromptCommand(Guid Id) : IRequest<PromptDetailDto>;
