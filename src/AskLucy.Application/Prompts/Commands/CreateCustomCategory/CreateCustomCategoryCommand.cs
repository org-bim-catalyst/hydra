using MediatR;

namespace AskLucy.Application.Prompts.Commands.CreateCustomCategory;

/// <summary>Creates a category private to the caller (spec.md FR-050, research.md Decision 6).</summary>
public sealed record CreateCustomCategoryCommand(string Name) : IRequest<PromptCategoryDto>;
