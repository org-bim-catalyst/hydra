using MediatR;

namespace AskLucy.Application.Prompts.Commands.DeletePrompt;

/// <summary>Soft-deletes a <see cref="AskLucy.Domain.Prompts.Prompt"/> (spec.md FR-001).</summary>
public sealed record DeletePromptCommand(Guid Id) : IRequest;
