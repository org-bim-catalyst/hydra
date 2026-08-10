using MediatR;

namespace AskLucy.Application.Prompts.Commands.RestorePrompt;

/// <summary>Restores an archived prompt to active status (spec.md FR-001).</summary>
public sealed record RestorePromptCommand(Guid Id) : IRequest;
