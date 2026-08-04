using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.CreateCustomCategory;

/// <summary>Creates a category private to the caller (FR-018/FR-038).</summary>
public sealed record CreateCustomCategoryCommand(string Name) : IRequest<KnowledgeBaseCategoryDto>;
