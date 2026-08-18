using MediatR;

namespace AskLucy.Application.Prompts.Commands.ImportPrompts;

/// <summary>Imports a previously-exported file, creating each entry as a brand-new, independent prompt (spec.md FR-071/FR-072, contracts/prompts-api.md).</summary>
public sealed record ImportPromptsCommand(PromptExportFile File) : IRequest<IReadOnlyList<PromptListItemDto>>;
