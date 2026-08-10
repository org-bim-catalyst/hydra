using MediatR;

namespace AskLucy.Application.Prompts.Commands.RestoreVersion;

/// <summary>Restores a prior version's content as the new current state (spec.md FR-033) — creates a new version, never deletes/overwrites history.</summary>
public sealed record RestoreVersionCommand(Guid PromptId, int VersionNumber) : IRequest<PromptDetailDto>;
