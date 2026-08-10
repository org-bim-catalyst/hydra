using MediatR;

namespace AskLucy.Application.Prompts.Commands.DuplicateVersion;

/// <summary>Creates a new, independent prompt seeded from a specific historical version (spec.md FR-032).</summary>
public sealed record DuplicateVersionCommand(Guid PromptId, int VersionNumber) : IRequest<PromptDetailDto>;
