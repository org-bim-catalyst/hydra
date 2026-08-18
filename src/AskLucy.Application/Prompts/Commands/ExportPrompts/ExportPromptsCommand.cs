using MediatR;

namespace AskLucy.Application.Prompts.Commands.ExportPrompts;

/// <summary>Exports one or more of the caller's own prompts as a portable file (spec.md FR-070, contracts/prompts-api.md).</summary>
public sealed record ExportPromptsCommand(IReadOnlyList<Guid> PromptIds) : IRequest<PromptExportFile>;
