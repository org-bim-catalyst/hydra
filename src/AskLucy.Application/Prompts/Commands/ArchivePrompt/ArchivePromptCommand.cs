using MediatR;

namespace AskLucy.Application.Prompts.Commands.ArchivePrompt;

/// <summary>Archives a prompt (spec.md FR-001, spec.md Edge Cases — an archived prompt stays usable for in-flight references but drops out of default listings).</summary>
public sealed record ArchivePromptCommand(Guid Id) : IRequest;
